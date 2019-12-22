﻿using InstantMessaging.Wrapper;
using InstaSharper.API;
using InstaSharper.API.Builder;
using InstaSharper.Classes;
using InstaSharper.Logger;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.UI.Core;
using InstantMessaging.Notification;
using InstaSharper.API.Push;
using InstaSharper.Classes.Models.Direct;
using InstaSharper.Enums;
using Microsoft.Toolkit.Uwp;

namespace InstantMessaging
{
    class ApiContainer : INotifyPropertyChanged
    {
        // Todo: handle exceptions thrown by _instaApi like no network connection
        private const string STATE_FILE_NAME = "state.bin";
        private const string PUSH_STATE_FILE_NAME = "push.bin";
        private const string SOCKET_ID = "mqtt_fbns";

        private IInstaApi _instaApi;
        private readonly StorageFolder _localFolder = ApplicationData.Current.LocalFolder;
        private StorageFile _stateFile;
        private FbnsConnectionData _pushData;
        private UserSessionData _userSession;
        private PushClient _pushClient;
        private DateTime _lastUpdated = DateTime.Now;

        public event PropertyChangedEventHandler PropertyChanged;

        public MainPage PageReference { get; set; }
        public InstaDirectInboxWrapper Inbox { get; private set; }

        public IncrementalLoadingCollection<InstaDirectInboxWrapper, InstaDirectInboxThreadWrapper> InboxThreads => Inbox.Threads;

        public InstaCurrentUserWrapper LoggedInUser { get; private set; }
        public InstaDirectInboxThreadWrapper SelectedThread { get; set; }
        public bool IsUserAuthenticated { get; private set; }
        public StateData StateData
        {
            get
            {
                var data = _instaApi?.GetStateData();
                if (data == null) return new StateData();
                data.FbnsConnectionData = _pushData;
                return data;
            }
        }

        private ApiContainer() {}

        public static async Task<ApiContainer> Factory()
        {
            var instance = new ApiContainer();
            await instance.CreateStateFile();
            await instance.LoadStateFromStorage();
            return instance;
        }

        public async Task OnLoggedIn()
        {
            if (!_instaApi.IsUserAuthenticated) throw new Exception("User is not logged in.");
            Inbox = new InstaDirectInboxWrapper(_instaApi);
            PageReference.DataContext = InboxThreads;
            await UpdateLoggedInUser();
            await StartPushClient();
        }

        public void SetSelectedThreadNull()
        {
            SelectedThread = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedThread)));
        }

        private async Task CreateStateFile()
        {
            _stateFile = (StorageFile) await _localFolder.TryGetItemAsync(STATE_FILE_NAME);
            if (_stateFile == null)
            {
                _stateFile = await _localFolder.CreateFileAsync(STATE_FILE_NAME, CreationCollisionOption.ReplaceExisting);
                IsUserAuthenticated = false;
            }
        }

        private async Task LoadStateFromStorage()
        {
            using (var stateStream = await _stateFile.OpenStreamForReadAsync())
            {
                if (stateStream.Length > 0)
                {
                    var formatter = new BinaryFormatter();
                    stateStream.Seek(0, SeekOrigin.Begin);
                    StateData stateData = (StateData) formatter.Deserialize(stateStream);
                    _instaApi = InstaApiBuilder.CreateBuilder()
                        .LoadStateData(stateData)
                        .UseLogger(new DebugLogger(LogLevel.All))
                        .Build();
                    IsUserAuthenticated = _instaApi.IsUserAuthenticated;
                    if(IsUserAuthenticated) _userSession = _instaApi.GetLoggedUser();
                    _pushData = stateData.FbnsConnectionData;
                }
                else
                {
                    _pushData = new FbnsConnectionData();
                }
            }
        }

        public async Task WriteStateToStorage()
        {
            using (var stateFileStream = await _stateFile.OpenStreamForWriteAsync())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(stateFileStream, StateData);
            }
        }

        /// <summary>
        /// Transfer socket as well as necessary context for background push notification client. 
        /// Transfer only happens if user is logged in.
        /// </summary>
        public async Task TransferPushSocket()
        {
            if (!_instaApi.IsUserAuthenticated) return;

            // Hand over MQTT socket to socket broker
            var memoryStream = new MemoryStream();
            var formatter = new BinaryFormatter();
            formatter.Serialize(memoryStream, StateData);
            var buffer = CryptographicBuffer.CreateFromByteArray(memoryStream.ToArray());
            await _pushClient.SendPing();
            await _pushClient.Shutdown();
            await _pushClient.Socket.CancelIOAsync();
            _pushClient.Socket.TransferOwnership(
                SOCKET_ID, 
                new SocketActivityContext(buffer), 
                TimeSpan.FromSeconds(PushClient.KEEP_ALIVE - 60));
        }

        public async Task<IResult<InstaLoginResult>> Login(string username, string password)
        {

            var session = new UserSessionData {UserName = username, Password = password};
            _instaApi = InstaApiBuilder.CreateBuilder()
                .SetUser(session)
                .UseLogger(new DebugLogger(LogLevel.All))
                .Build();

            var logInResult = await _instaApi.LoginAsync();
            await WriteStateToStorage();
            _userSession = _instaApi.GetLoggedUser();
            return logInResult;
        }

        public async Task<IResult<bool>> Logout()
        {
            var result = await _instaApi.LogoutAsync();
            await _pushClient.Shutdown();
            _pushData = null;
            if (result.Value)
                await WriteStateToStorage();
            return result;
        }

        private async Task UpdateLoggedInUser()
        {
            var loggedInUser = await _instaApi.GetCurrentUserAsync();
            LoggedInUser = new InstaCurrentUserWrapper(loggedInUser.Value, _instaApi);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoggedInUser)));
        }

        /// <summary>
        /// Start foreground push notification client from existing socket (transferred from background task) or create a new one.
        /// </summary>
        public async Task StartPushClient()
        {
            if (!_instaApi.IsUserAuthenticated) return;
            _pushClient = new PushClient(_instaApi, _pushData);
            _pushClient.MessageReceived += OnNotificationReceived;
            try
            {
                if (SocketActivityInformation.AllSockets.TryGetValue(SOCKET_ID, out var socketInformation))
                {
                    var dataStream = socketInformation.Context.Data.AsStream();
                    var formatter = new BinaryFormatter();
                    var stateData = (StateData)formatter.Deserialize(dataStream);
                    _pushData = stateData.FbnsConnectionData;
                    var socket = socketInformation.StreamSocket;
                    if (string.IsNullOrEmpty(_pushData.FbnsToken)) // if we don't have any push data, start fresh
                        await _pushClient.Start();
                    else
                        await _pushClient.StartWithExistingSocket(socket);
                }
                else
                {
                    await _pushClient.Start();
                }
            }
            catch (Exception)
            {
                await _pushClient.Start();
            }
        }

        private async void OnNotificationReceived(object sender, MessageReceivedEventArgs args)
        {
            Debug.WriteLine("Notification received.");
            if (DateTime.Now - _lastUpdated > TimeSpan.FromSeconds(0.1))
            {
                await PageReference.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                    async () => await UpdateInboxAndSelectedThread());
            }
        }

        public async Task UpdateSelectedThread()
        {
            if (SelectedThread == null)
                return;
            var result = await _instaApi.MessagingProcessor.GetDirectInboxThreadAsync(SelectedThread.ThreadId, PaginationParameters.MaxPagesToLoad(1));
            if (result.Succeeded)
            {
                SelectedThread.Update(result.Value);
            }
        }

        // Send message to the current selected recipient
        public async Task<IResult<InstaDirectInboxThreadList>> SendMessage(string content)
        {
            var result = await _instaApi.MessagingProcessor.SendDirectTextAsync(SelectedThread.Users.FirstOrDefault()?.Pk.ToString(), SelectedThread.ThreadId, content);
            await PageReference.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                async () => { await UpdateInboxAndSelectedThread(); });
            return result;
        }

        public async Task UpdateInboxAndSelectedThread()
        {
            var selected = SelectedThread;
            await Inbox.UpdateInbox();
            if (selected == null) return;
            if (InboxThreads.Contains(selected) && SelectedThread != selected) SelectedThread = selected;
            await UpdateSelectedThread();
            _lastUpdated = DateTime.Now;
        }
    }
}
