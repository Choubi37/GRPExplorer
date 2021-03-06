﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Media.Animation;
using GRPExplorerLib;
using GRPExplorerLib.BigFile;
using GRPExplorerLib.Util;
using System.ComponentModel;
using GRPExplorerGUI.Model;
using GRPExplorerGUI.ViewModel;
using GRPExplorerLib.Logging;
using GRPExplorerLib.BigFile.Extra;
using GRPExplorerGUI.Extra;
using Microsoft.Win32;
using System.IO;

namespace GRPExplorerGUI
{
    public partial class MainWindow : Window
    {
        private string lastText = "";

        private FEUtoSWFWindow FEUtoSWFWindow;

        IGRPExplorerLibLogInterface logInterface;

        public MainWindow()
        {
            InitializeComponent();

            btnFindKey.Click += BtnFindKey_Click;
            
            LogModel logModel = new LogModel();
            logInterface = logModel.LogProxy;
            logView.Log = logModel;
            LogManager.LogInterface = logInterface;
            LogManager.GlobalLogFlags = LogFlags.Info | LogFlags.Error;

            TypewriteTextblock("...................", lblLoadingEllipses, new TimeSpan(0, 0, 2));
        }

        private void BtnFindKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int val = Convert.ToInt32(txtFindKey.Text, 16);
                lastText = txtFindKey.Text;

                SetFile(val);
            }
            catch (Exception ex)
            {
                txtFindKey.Text = lastText;
                Log(ex.Message);
            }
        }

        private void SetFile(int key)
        {
            if (bigFileview.BigFileViewModel == null)
            {
                Log("no.");
                return;
            }

            YetiObject file = bigFileview.BigFileViewModel.BigFile.FileMap[key];
            if (file != null)
            {
                bigFileview.FolderTree.SelectedFile = file;
            }
            else
            {
                Log("File key not found!");
            }
        }

        private void Log(string msg)
        {
            logInterface.Info("[GUI] " + msg);
        }
        
        BigFileUnpacker unpacker;
        private void UnpackBigfile(string path, bool compress)
        {
            BigFileUnpackOptions options = new BigFileUnpackOptions
            {
                Directory = new System.IO.DirectoryInfo(path),
                Flags = BigFileFlags.Decompress | BigFileFlags.UseThreading,
                Threads = 1,
            };

            unpacker = new BigFileUnpacker(bigFileview.BigFileViewModel.BigFile);

            BigFileUnpackOperationStatus status = unpacker.UnpackBigfile(options);
        }

        BigFilePacker packer;
        private void PackBigfile(string path, bool compress)
        {
            BigFilePackOptions options = new BigFilePackOptions
            {
                Directory = new System.IO.DirectoryInfo(path),
                Flags = compress ? BigFileFlags.UseThreading | BigFileFlags.Compress : BigFileFlags.UseThreading,
                Threads = 4,
                BigFileName = "Yeti",
                DeleteChunks = true
            };

            logInterface.Info("[GUI] Threads: " + options.Threads);

            packer = new BigFilePacker(bigFileview.BigFileViewModel.BigFile);

            BigFilePackOperationStatus status = packer.PackBigFile(options);
        }

        private void MenuOpenBigfile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                DefaultExt = ".big",
                Filter = "BIG files (*.big)|*.big",
                InitialDirectory = Settings.LastBigfilePath
            };

            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                Settings.LastBigfilePath = Path.GetDirectoryName(dialog.FileName);

                BigFile bf = new PackedBigFile(new FileInfo(dialog.FileName));
                bigFileview.BigFileViewModel = new BigFileViewModel
                {
                    BigFile = bf
                };

                stkLoadingReferences.Visibility = Visibility.Visible;
                lblLoadingReferences.Content = "Loading bigfile";

                bigFileview.BigFileViewModel.LoadFromDisk
                    (() =>
                    {
                        bf.FileUtil.SortFolderTree(bf.RootFolder);
                        bigFileview.FolderTree.RootFolder = bigFileview.BigFileViewModel.BigFile.RootFolder;

                        lblLoadingReferences.Content = "Loading references";

                        bigFileview.BigFileViewModel.LoadExtraData
                            (() =>
                            {
                                stkLoadingReferences.Visibility = Visibility.Collapsed;
                                LogManager.Info("REFERENCES LOADED");
                            });
                    });
            }
        }

        private void MenuOpenUnpackedBigfile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                DefaultExt = ".gex",
                Filter = "GEX files (*.gex)|*.gex",
                InitialDirectory = Settings.LastUnpackedBigfilePath
            };

            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                Settings.LastUnpackedBigfilePath = Path.GetDirectoryName(dialog.FileName);

                BigFile bf = new UnpackedBigFile(new DirectoryInfo(Path.GetDirectoryName(dialog.FileName)));
                bigFileview.BigFileViewModel = new BigFileViewModel
                {
                    BigFile = bf
                };

                stkLoadingReferences.Visibility = Visibility.Visible;
                lblLoadingReferences.Content = "Loading bigfile";

                bigFileview.BigFileViewModel.LoadFromDisk
                    (() =>
                    {
                        bf.FileUtil.SortFolderTree(bf.RootFolder);
                        bigFileview.FolderTree.RootFolder = bigFileview.BigFileViewModel.BigFile.RootFolder;

                        lblLoadingReferences.Content = "Loading references";

                        bigFileview.BigFileViewModel.LoadExtraData
                            (() =>
                            {
                                stkLoadingReferences.Visibility = Visibility.Collapsed;
                                LogManager.Info("REFERENCES LOADED");
                            });
                    });
            }
        }

        private void MenuFEUtoSWF_Click(object sender, RoutedEventArgs e)
        {
            FEUtoSWFWindow = new FEUtoSWFWindow();
            FEUtoSWFWindow.ShowDialog();
        }

        private void MenuBigfilePacking_Click(object sender, RoutedEventArgs e)
        {
            BigfilePackingWindow window = new BigfilePackingWindow();
            window.ShowDialog();
            if (window.Clicked == clicked.pack)
                PackBigfile(window.txtUnpackDir.Text, window.chkCompress.IsChecked ==  true);
            if (window.Clicked == clicked.unpack)
                UnpackBigfile(window.txtUnpackDir.Text, window.chkCompress.IsChecked == true);
        }

        private void TypewriteTextblock(string textToAnimate, Label txt, TimeSpan timeSpan)
        {
            Storyboard story = new Storyboard();
            story.FillBehavior = FillBehavior.HoldEnd;
            story.RepeatBehavior = RepeatBehavior.Forever;

            DiscreteStringKeyFrame discreteStringKeyFrame;
            StringAnimationUsingKeyFrames stringAnimationUsingKeyFrames = new StringAnimationUsingKeyFrames();
            stringAnimationUsingKeyFrames.Duration = new Duration(timeSpan);

            string tmp = string.Empty;
            foreach (char c in textToAnimate)
            {
                discreteStringKeyFrame = new DiscreteStringKeyFrame();
                discreteStringKeyFrame.KeyTime = KeyTime.Paced;
                tmp += c;
                discreteStringKeyFrame.Value = tmp;
                stringAnimationUsingKeyFrames.KeyFrames.Add(discreteStringKeyFrame);
            }
            Storyboard.SetTargetName(stringAnimationUsingKeyFrames, txt.Name);
            Storyboard.SetTargetProperty(stringAnimationUsingKeyFrames, new PropertyPath(Label.ContentProperty));
            story.Children.Add(stringAnimationUsingKeyFrames);

            story.Begin(txt);
        }
    }
}
