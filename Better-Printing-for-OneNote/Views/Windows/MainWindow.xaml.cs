﻿using Better_Printing_for_OneNote.ViewModels;
using System;
using System.Windows;

namespace Better_Printing_for_OneNote.Views.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow(string argFilePath)
        {
            var viewModel = new MainWindowViewModel(argFilePath, this);
            DataContext = viewModel;

            InitializeComponent();

            // register event handlers
            PreviewKeyDown += (sender, e) =>
            {
                    MainIFDV.OnApplication_PreviewKeyDown(sender, e);
            };
            Closing += viewModel.OnWindowClosing;
        }

        private void BringWindowToFront(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsVisible)
                {
                    Show();
                }
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }
                Activate();
                Topmost = true;
                Topmost = false;
                Focus();
            });
        }
    }
}
