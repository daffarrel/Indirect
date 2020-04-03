﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using InstagramAPI.Classes.User;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Indirect
{
    public sealed partial class ProfilePicture : UserControl
    {
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
            nameof(Source),
            typeof(ObservableCollection<InstaUser>),
            typeof(ProfilePicture),
            new PropertyMetadata(null, OnSourceChanged));
        public static readonly DependencyProperty IsUserActiveProperty = DependencyProperty.Register(
            nameof(IsUserActive),
            typeof(bool),
            typeof(ProfilePicture),
            new PropertyMetadata(null, OnUserActiveChanged));


        public ObservableCollection<InstaUser> Source
        {
            get => (ObservableCollection<InstaUser>)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public bool IsUserActive
        {
            get => (bool) GetValue(IsUserActiveProperty);
            set => SetValue(IsUserActiveProperty, value);
        }

        private static void OnUserActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ProfilePicture)d;
            var isActive = (bool) e.NewValue;
            view.ActiveIndicator.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ProfilePicture)d;
            var item = (ObservableCollection<InstaUser>) e.NewValue;
            if (item.Count > 1)
            {
                view.Single.Visibility = Visibility.Collapsed;
                view.Group.Visibility = Visibility.Visible;
                view.Person1.Source = item[0].ProfilePictureUrl;
                view.Person2.Source = item[1].ProfilePictureUrl;
            }
            else
            {
                view.Single.Visibility = Visibility.Visible;
                view.Group.Visibility = Visibility.Collapsed;
                view.Single.Source = item[0]?.ProfilePictureUrl;
            }
            if (e.OldValue == null) view.ViewModelOnPropertyChanged(view, new PropertyChangedEventArgs(string.Empty));
        }

        public ProfilePicture()
        {
            this.InitializeComponent();
            ApiContainer.Instance.PropertyChanged += ViewModelOnPropertyChanged;
        }

        private void ViewModelOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ApiContainer.UserPresenceDictionary) && !string.IsNullOrEmpty(e.PropertyName)) return;
            if (Source == null) return;
            if (Source.Any(user => ApiContainer.Instance.UserPresenceDictionary.TryGetValue(user.Pk, out var value) && value))
            {
                IsUserActive = true;
                return;
            }

            IsUserActive = false;
        }

        private void ProfilePicture_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            const double groupImageRatio = 0.8;
            Single.Width = e.NewSize.Width;
            Single.Height = e.NewSize.Height;
            Person1.Width = Person2.Width = e.NewSize.Width * groupImageRatio;
            Person1.Height = Person2.Height = e.NewSize.Height * groupImageRatio;
            Person1.Margin = new Thickness((1 - groupImageRatio) * e.NewSize.Width, 0, 0, (1 - groupImageRatio) * e.NewSize.Height);
            Person2.Margin = new Thickness(0, (1 - groupImageRatio) * e.NewSize.Height, (1 - groupImageRatio) * e.NewSize.Width, 0);
        }
    }
}
