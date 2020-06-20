﻿using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using InstagramAPI;
using InstagramAPI.Classes.Media;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Indirect.Controls
{
    public sealed partial class AnimatedImagePicker : UserControl
    {
        public event EventHandler<GiphyMedia> ImageSelected; 

        public ObservableCollection<GiphyMedia> ImageList { get; } = new ObservableCollection<GiphyMedia>();

        private GiphyMedia[] _stickers;
        private GiphyMedia[] _gifs;
        private string _selectedType;
        private CancellationTokenSource _searchDebounce = new CancellationTokenSource();

        public AnimatedImagePicker()
        {
            this.InitializeComponent();
        }

        private async void TypeSelectBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var typeName = e.AddedItems[0].ToString().ToLower();
            if (typeName == _selectedType) return;
            _selectedType = typeName;
            switch (typeName)
            {
                case "sticker" when _stickers != null:
                    PrepareImagesForDisplay(_stickers);
                    return;
                case "gif" when _gifs != null:
                    PrepareImagesForDisplay(_gifs);
                    return;
                default:
                    await SearchAnimatedImage();
                    break;
            }
        }

        private async void SearchBox_OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            if (await SearchReady())
            {
                await SearchAnimatedImage();
            }
        }

        private async Task SearchAnimatedImage()
        {
            if (string.IsNullOrEmpty(_selectedType)) return;
            var query = SearchBox.Text;
            switch (_selectedType)
            {
                case "sticker":
                    {
                        var result = await Instagram.Instance.SearchAnimatedImageAsync(query, AnimatedImageType.Sticker);
                        if (result.Value == null) return;
                        _stickers = result.Value;
                        PrepareImagesForDisplay(_stickers);
                    }
                    break;
                case "gif":
                    {
                        var result = await Instagram.Instance.SearchAnimatedImageAsync(query, AnimatedImageType.Gif);
                        if (result.Value == null) return;
                        _gifs = result.Value;
                        PrepareImagesForDisplay(_gifs);
                    }
                    break;
                default:
                    throw new NotImplementedException($"{_selectedType} is not a supported animated image type");
            }
        }

        private void PrepareImagesForDisplay(GiphyMedia[] source)
        {
            if (source == null) return;
            ImageList.Clear();
            foreach (var media in source)
            {
                ImageList.Add(media);
            }
            if (ImageList.Count > 0)
            {
                PickerView.UpdateLayout();
                PickerView.ScrollIntoView(ImageList[0]);
            }
        }

        private async Task<bool> SearchReady()
        {
            _searchDebounce?.Cancel();
            _searchDebounce?.Dispose();
            _searchDebounce = new CancellationTokenSource();
            var cancellationToken = _searchDebounce.Token;
            try
            {
                await Task.Delay(500, cancellationToken); // Delay so we don't search something mid typing
            }
            catch (TaskCanceledException)
            {
                return false;
            }
            if (cancellationToken.IsCancellationRequested) return false;
            return true;
        }

        private async void SearchBox_OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            await SearchAnimatedImage();
        }

        private void TypeSelectBox_OnLoaded(object sender, RoutedEventArgs e)
        {
            TypeSelectBox.SelectedIndex = 0;
        }

        private async void PickerOnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0 || e.AddedItems[0] == null) return;
            var image = (GiphyMedia)e.AddedItems[0];
            await ApiContainer.Instance.SendAnimatedImage(image.Id, image.IsSticker);
            var gridView = (GridView) sender;
            gridView.SelectedItem = null;
            ImageSelected?.Invoke(this, image);
        }
    }
}
