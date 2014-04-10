﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using ErrorCode = Twitch.ErrorCode;
using AuthToken = Twitch.AuthToken;

namespace WinformsSample
{
    public partial class SampleForm : Form
    {
        protected readonly string kClientId = "ClientId.txt";
        protected readonly string kClientSecret = "ClientSecret.txt";
        protected readonly string kAuthTokenFile = "AuthToken.txt";

        protected Twitch.Broadcast.WinFormsBroadcastController mBroadcastController = new Twitch.Broadcast.WinFormsBroadcastController();
        protected Twitch.Chat.WinFormsChatController mChatController = new Twitch.Chat.WinFormsChatController();
        protected Twitch.Test.TestFrameGenerator mFrameGenerator = null;
        protected Twitch.Broadcast.IngestTester mIngestTester = null;

        protected Thread mAudioThread = null; // Used for passthrough audio only
        protected bool mShuttingDown = false;

        public SampleForm()
        {
            InitializeComponent();

            mSetAuthTokenButton.Enabled = System.IO.File.Exists(kAuthTokenFile);

            // force a state update
            HandleBroadcastStateChanged(Twitch.Broadcast.BroadcastController.BroadcastState.Uninitialized);
            mChatMessagesGroupbox.Enabled = false;

            mMaxKbpsTrackbar.Minimum = (int)Twitch.Broadcast.Constants.TTV_MIN_BITRATE;
            mMaxKbpsTrackbar.Maximum = (int)Twitch.Broadcast.Constants.TTV_MAX_BITRATE;
            mMaxKbpsTrackbar.Value = 1500;

            mAspectRatioCombo.SelectedIndex = 0;
            mResolutionCombo.SelectedIndex = 0;

            if (System.IO.File.Exists(kClientId))
            {
                mClientIdText.Text = System.IO.File.ReadAllText(kClientId);
            }
            if (System.IO.File.Exists(kClientSecret))
            {
                mClientSecretText.Text = System.IO.File.ReadAllText(kClientSecret);
            }

            {
                Twitch.Broadcast.BroadcastController.GameAudioCaptureMethod[] arr = (Twitch.Broadcast.BroadcastController.GameAudioCaptureMethod[])Enum.GetValues(typeof(Twitch.Broadcast.BroadcastController.GameAudioCaptureMethod));
                mAudioCaptureMethodCombo.Items.Clear();
                foreach (var i in arr)
                {
                    mAudioCaptureMethodCombo.Items.Add(i);
                }
                mAudioCaptureMethodCombo.SelectedIndex = 0;
            }
            {
                Twitch.Chat.ChatController.EmoticonMode[] arr = (Twitch.Chat.ChatController.EmoticonMode[])Enum.GetValues(typeof(Twitch.Chat.ChatController.EmoticonMode));
                mEmoticonModeCombobox.Items.Clear();
                foreach (var i in arr)
                {
                    mEmoticonModeCombobox.Items.Add(i);
                }
                mEmoticonModeCombobox.SelectedIndex = 0;
            }

            mBroadcastController.AuthTokenRequestComplete += this.HandleAuthTokenRequestComplete;
            mBroadcastController.StreamInfoUpdated += this.HandleStreamInfoUpdated;
            mBroadcastController.LoginAttemptComplete += this.HandleLoginAttemptComplete;
            mBroadcastController.GameNameListReceived += this.HandleGameNameListReceived;
            mBroadcastController.BroadcastStateChanged += this.HandleBroadcastStateChanged;
            mBroadcastController.IngestListReceived += this.HandleIngestListReceived;
            mBroadcastController.BroadcastStarted += this.HandleBroadcastStarted;
            mBroadcastController.BroadcastStopped += this.HandleBroadcastStopped;

            mChatController.TokenizedMessagesReceived += this.HandleTokenizedMessagesReceived;
            mChatController.RawMessagesReceived += this.HandleRawMessagesReceived;
            mChatController.UsersChanged += this.HandleUsersChanged;
            mChatController.ChatStateChanged += HandleChatStateChanged;
            mChatController.MessagesCleared += this.HandleClearMessages;
            mChatController.EmoticonDataAvailable += this.HandleEmoticonDataAvailable;
            mChatController.EmoticonDataExpired += this.HandleEmoticonDataExpired;
            mChatController.Connected += this.HandleChatConnected;
            mChatController.Disconnected += this.HandleChatDisconnected;

            mAudioThread = new Thread(SubmitAudioSamples);
            mAudioThread.Start();
        }

        #region Stream

        private void SubmitAudioSamples()
        {
            const int sampleRate = 44100;
            const int numChannels = 2;
            const float pitchBase = 440.0f; // Hz, perfect A
            const int chunkDurationMilliseconds = 20;
            const float pitchFrequency = 5;
            const float TWO_PI = 2 * (float)Math.PI;

            const int numSamplesPerChunk = numChannels * sampleRate / (1000 / chunkDurationMilliseconds);
            const float pitchStep = TWO_PI / (float)sampleRate / pitchFrequency;

            short[] chunk = new short[numSamplesPerChunk];
            float waveFormProgress = 0;
            float pitchProgress = 0;

            System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
            timer.Start();

            long nextSubmit = timer.ElapsedMilliseconds;

            while (!mShuttingDown)
            {
                if (mBroadcastController.AudioCaptureMethod == Twitch.Broadcast.BroadcastController.GameAudioCaptureMethod.Passthrough)
                {
                    for (int i = 0; i < numSamplesPerChunk; i += 2)
                    {
                        float pitch = pitchBase + 100.0f * (float)Math.Sin(pitchProgress);
                        float waveFormStep = TWO_PI * (float)pitch / (float)sampleRate;

                        float sample = (float)Math.Sin(waveFormProgress) * 32767.0f;
                        chunk[i] = (short)sample;
                        chunk[i + 1] = chunk[i];

                        waveFormProgress += waveFormStep;
                        pitchProgress += pitchStep;

                        if (waveFormProgress >= TWO_PI)
                        {
                            waveFormProgress -= TWO_PI;
                        }
                        if (pitchProgress >= TWO_PI)
                        {
                            pitchProgress -= TWO_PI;
                        }
                    }

                    while (timer.ElapsedMilliseconds < nextSubmit)
                    {
                        Thread.Sleep(0);
                    }

                    nextSubmit += chunkDurationMilliseconds;
                    mBroadcastController.SubmitAudioSamples(chunk);
                }
                else
                {
                    Thread.Sleep(chunkDurationMilliseconds);
                }
            }
        }

        private void InitButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.ClientId = mClientIdText.Text;
            mBroadcastController.Initialize();

            System.IO.File.WriteAllText(kClientId, mClientIdText.Text);
            System.IO.File.WriteAllText(kClientSecret, mClientSecretText.Text);

            mMicVolumeText.Text = mBroadcastController.MicrophoneVolume.ToString();
            mSystemVolumeText.Text = mBroadcastController.SystemVolume.ToString();
        }

        private void ShutdownButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.Shutdown();
        }

        private void StreamTasksTimer_Tick(object sender, EventArgs e)
        {
            mBroadcastController.Update();

            mArchivingStateCheckbox.Checked = mBroadcastController.ArchivingState.RecordingEnabled;

            if (mIngestTester != null)
            {
                if (mIngestTester.State == Twitch.Broadcast.IngestTester.TestState.TestingServer)
                {
                    mIngestTestStatusText.Text = "[" + (int)(mIngestTester.TotalProgress * 100) + "%] " + mIngestTester.State.ToString() + ": " + mIngestTester.CurrentServer.ServerName + "... " + mIngestTester.CurrentServer.BitrateKbps + " kbps [" + (int)(mIngestTester.ServerProgress * 100) + "%]";
                    RefreshListbox(mIngestListListbox);
                }
            }
        }

        private void mRequestAuthTokenButton_Click(object sender, EventArgs e)
        {
            LoginForm frm = new LoginForm();
            frm.ShowPasswordField = true;
            DialogResult result = frm.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            mBroadcastController.ClientSecret = mClientSecretText.Text;
            mBroadcastController.RequestAuthToken(frm.UserName, frm.Password);
        }

        private void mSetAuthTokenButton_Click(object sender, EventArgs e)
        {
            LoginForm frm = new LoginForm();
            frm.ShowPasswordField = false;
            DialogResult result = frm.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            // attempt to read the previously cached authtoken from file

            string str = null;
            try
            {
                str = System.IO.File.ReadAllText(kAuthTokenFile);
            }
            catch (Exception x)
            {
                MessageBox.Show(x.ToString());
                return;
            }

            AuthToken token = new AuthToken(str);

            mBroadcastController.ClientSecret = mClientSecretText.Text;
            mBroadcastController.SetAuthToken(frm.UserName, token);
        }

        private void mStartButton_Click(object sender, EventArgs e)
        {
            if (mResolutionCombo.SelectedItem == null)
            {
                return;
            }

            string[] resolution = mResolutionCombo.SelectedItem.ToString().Split(new char[]{'x'});
            uint width = uint.Parse(resolution[0]);
            uint height = uint.Parse(resolution[1]);
            uint fps = (uint)mFramesPerSecondSelector.Value;

            Twitch.Broadcast.VideoParams videoParams = mBroadcastController.GetRecommendedVideoParams(width, height, fps);
            mBroadcastController.AudioCaptureMethod = (Twitch.Broadcast.BroadcastController.GameAudioCaptureMethod)mAudioCaptureMethodCombo.SelectedItem;
            mBroadcastController.CaptureMicrophone = mCaptureMicrophoneCheckbox.Checked;

            if (!mBroadcastController.StartBroadcasting(videoParams))
            {
                return;
            }

            mFrameGenerator = new Twitch.Test.TestFrameGenerator(width, height);
        }

        private void mStartButtonRecommended_Click(object sender, EventArgs e)
        {
            uint fps = (uint)mFramesPerSecondSelector.Value;
            uint maxKbps = (uint)mMaxKbpsTrackbar.Value;

            string[] arr = mAspectRatioCombo.Text.Split(':');
            float aspectRatio = float.Parse(arr[0]) / float.Parse(arr[1]);

            Twitch.Broadcast.VideoParams videoParams = mBroadcastController.GetRecommendedVideoParams(maxKbps, fps, 0.1f, aspectRatio);
            mBroadcastController.AudioCaptureMethod = (Twitch.Broadcast.BroadcastController.GameAudioCaptureMethod)mAudioCaptureMethodCombo.SelectedItem;
            mBroadcastController.CaptureMicrophone = mCaptureMicrophoneCheckbox.Checked;

            if (!mBroadcastController.StartBroadcasting(videoParams))
            {
                return;
            }

            mSubmitFrameTimer.Interval = 1000 / (int)fps;

            mFrameGenerator = new Twitch.Test.TestFrameGenerator(videoParams.OutputWidth, videoParams.OutputHeight);
        }

        private void mStopButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.StopBroadcasting();
        }

        private void mSetStreamInfoButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.SetStreamInfo(mChannelNameText.Text, mGameNameText.Text, mStreamTitleText.Text);
        }

        private void SampleForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            mShuttingDown = true;
            mChatController.ForceSyncShutdown();
            mBroadcastController.ForceSyncShutdown();
        }

        private void mSubmitFrameTimer_Tick(object sender, EventArgs e)
        {
            if (mBroadcastController.IsPaused || mFrameGenerator == null)
            {
                return;
            }

            UIntPtr buffer = mBroadcastController.GetNextFreeBuffer();
            if (buffer == null || buffer == UIntPtr.Zero)
            {
                return;
            }

            mFrameGenerator.Generate(buffer);

            mBroadcastController.SubmitFrame(buffer);
        }

        private void mPauseButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.PauseBroadcasting();
        }

        private void mRunCommercialButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.RunCommercial();
        }

        private void mResumeButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.ResumeBroadcasting();
        }

        private void mSendActionMetaDataButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.SendActionMetaData("test_action", mBroadcastController.CurrentBroadcastTime, "human", "{ \"awesome\" : 1 }");
        }

        protected ulong mMetaDataSpanSequenceid = 0xFFFFFFFFFFFFFFFF;

        private void mSendStartSpanMetaDataButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.StartSpanMetaData("test_span", mBroadcastController.CurrentBroadcastTime, out mMetaDataSpanSequenceid, "Test Span!", "{ \"Blah\" : \"Start\" }");
        }

        private void mSendEndSpanMetaDataButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.EndSpanMetaData("test_span", mBroadcastController.CurrentBroadcastTime, mMetaDataSpanSequenceid, "Test Span!", "{ \"Blah\" : \"End\" }");
            mMetaDataSpanSequenceid = 0xFFFFFFFFFFFFFFFF;
        }

        private void mGameNameListButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.RequestGameNameList(mGameNameListTextbox.Text);
        }

        private void mIncreaseMicVolumeButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.MicrophoneVolume += 0.1f;

            mMicVolumeText.Text = mBroadcastController.MicrophoneVolume.ToString();
        }

        private void mDecreaseMicVolumeButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.MicrophoneVolume -= 0.1f;

            mMicVolumeText.Text = mBroadcastController.MicrophoneVolume.ToString();
        }

        private void mIncreaseSysVolumeButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.SystemVolume += 0.1f;

            mSystemVolumeText.Text = mBroadcastController.SystemVolume.ToString();
        }

        private void mDecreaseSysVolumeButton_Click(object sender, EventArgs e)
        {
            mBroadcastController.SystemVolume -= 0.1f;

            mSystemVolumeText.Text = mBroadcastController.SystemVolume.ToString();
        }

        private void mStartIngestTestButton_Click(object sender, EventArgs e)
        {
            if (mIngestTester != null)
            {
                return;
            }

            mIngestTester = mBroadcastController.StartIngestTest();
            if (mIngestTester != null)
            {
                mIngestTester.OnTestStateChanged += mIngestTester_OnTestStateChanged;
            }
        }

        private void mMaxKbpsTrackbar_ValueChanged(object sender, EventArgs e)
        {
            mMaxKbpsLabel.Text = mMaxKbpsTrackbar.Value.ToString() + " Kbps";
        }

        void mIngestTester_OnTestStateChanged(Twitch.Broadcast.IngestTester source, Twitch.Broadcast.IngestTester.TestState state)
        {
            mIngestTestStatusText.Text = "[" + (int)(mIngestTester.TotalProgress * 100) + "%] " + state.ToString();

            switch (state)
            {
                case Twitch.Broadcast.IngestTester.TestState.ConnectingToServer:
                {
                    mIngestTestStatusText.Text += ": " + source.CurrentServer.ServerName + "...";
                    break;
                }
                case Twitch.Broadcast.IngestTester.TestState.TestingServer:
                case Twitch.Broadcast.IngestTester.TestState.DoneTestingServer:
                {
                    mIngestTestStatusText.Text += ": " + source.CurrentServer.ServerName + "... " + source.CurrentServer.BitrateKbps + " kbps";
                    break;
                }
                case Twitch.Broadcast.IngestTester.TestState.Finished:
                case Twitch.Broadcast.IngestTester.TestState.Cancelled:
                {
                    mIngestTester.OnTestStateChanged -= mIngestTester_OnTestStateChanged;
                    mIngestTester = null;
                    break;
                }
                default:
                {
                    break;
                }
            }

            RefreshListbox(mIngestListListbox);
        }

        private void mCancelIngestTestButton_Click(object sender, EventArgs e)
        {
            if (mIngestTester == null)
            {
                return;
            }

            mIngestTester.Cancel();
        }

        private void mSkipIngestServerButton_Click(object sender, EventArgs e)
        {
            if (mIngestTester == null)
            {
                return;
            }

            mIngestTester.SkipCurrentServer();
        }

        private void mIngestListListbox_Click(object sender, EventArgs e)
        {
            if (mIngestListListbox.SelectedItem == null)
            {
                return;
            }

            foreach (object obj in mIngestListListbox.Items)
            {
                (obj as IngestListEntry).Selected = false;
            }

            IngestListEntry entry = mIngestListListbox.SelectedItem as IngestListEntry;
            mBroadcastController.IngestServer = entry.Server;
            entry.Selected = true;

            RefreshListbox(mIngestListListbox);
        }

        protected void RefreshListbox(ListBox listbox)
        {
            typeof(ListBox).InvokeMember("RefreshItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.InvokeMethod, null, listbox, new object[] { });
        }

        #region Callbacks

        protected void HandleAuthTokenRequestComplete(ErrorCode result, AuthToken authToken)
        {
            if (Twitch.Error.Failed(result))
            {
                MessageBox.Show("Auth token request failed, please check your username and password and try again");
            }
            else
            {
                // cache the auth token for reuse on consecutive runs
                try
                {
                    System.IO.File.WriteAllText(kAuthTokenFile, authToken.Data);
                }
                catch (Exception x)
                {
                    MessageBox.Show(x.ToString());
                }
            }
        }

        protected void HandleStreamInfoUpdated(Twitch.Broadcast.StreamInfo info)
        {
            // TODO: got the stream info, do something with it
        }

        protected void HandleLoginAttemptComplete(ErrorCode result)
        {
            if (Twitch.Error.Failed(result))
            {
                MessageBox.Show("Login failed, please request another auth token");
            }

            mChannelNameText.Text = mBroadcastController.UserName;
            mChatChannelText.Text = mBroadcastController.UserName;
        }

        protected void HandleGameNameListReceived(ErrorCode result, Twitch.Broadcast.GameInfo[] list)
        {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < list.Length; ++i)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(list[i].Name);
            }

            MessageBox.Show(sb.ToString());
        }

        protected void HandleBroadcastStateChanged(Twitch.Broadcast.BroadcastController.BroadcastState state)
        {
            this.Text = "Twitch Winforms Sample - " + state.ToString();

            mSimpleBroadcastGroupbox.Enabled = state == Twitch.Broadcast.BroadcastController.BroadcastState.ReadyToBroadcast;
            mAdvancedBroadcastGroupbox.Enabled = state == Twitch.Broadcast.BroadcastController.BroadcastState.ReadyToBroadcast;
            mBroadcastControlsGroupbox.Enabled = state == Twitch.Broadcast.BroadcastController.BroadcastState.Broadcasting || state == Twitch.Broadcast.BroadcastController.BroadcastState.Paused;
            mIngestTestingGroupbox.Enabled = state == Twitch.Broadcast.BroadcastController.BroadcastState.ReadyToBroadcast || state == Twitch.Broadcast.BroadcastController.BroadcastState.IngestTesting;
            mAudioGroupbox.Enabled = state != Twitch.Broadcast.BroadcastController.BroadcastState.Uninitialized;
            mGameNameListGroupbox.Enabled = state != Twitch.Broadcast.BroadcastController.BroadcastState.Uninitialized;
            mLoginGroupbox.Enabled = mBroadcastController.IsInitialized && !mBroadcastController.IsLoggedIn;
            mBroadcastInfoGroupbox.Enabled = mBroadcastController.IsLoggedIn;

            mChatConnectionGroupbox.Enabled = mBroadcastController.IsInitialized && mBroadcastController.IsLoggedIn;
        }

        protected void HandleBroadcastStarted()
        {
            mSubmitFrameTimer.Enabled = true;
            mMetaDataGroup.Enabled = true;
        }

        protected void HandleBroadcastStopped()
        {
            mSubmitFrameTimer.Enabled = false;
            mFrameGenerator = null;
            mMetaDataGroup.Enabled = false;
        }

        protected class IngestListEntry
        {
            protected Twitch.Broadcast.IngestServer mServer = null;
            public bool Default { get; set; }
            public bool Selected { get; set; }

            public Twitch.Broadcast.IngestServer Server
            {
                get { return mServer; }
            }

            public IngestListEntry(Twitch.Broadcast.IngestServer server)
            {
                mServer = server;
            }

            public override string ToString()
            {
                string str = mServer.ServerName;
                if (this.Default) str += " (Default)";
                if (this.Selected) str = "* " + str;
                str += " - " + mServer.BitrateKbps + " kbps";

                return str;
            }
        }

        protected void HandleIngestListReceived(Twitch.Broadcast.IngestList list)
        {
            mIngestListListbox.Items.Clear();

            for (int i = 0; i < list.Servers.Length; ++i)
            {
                IngestListEntry item = new IngestListEntry(list.Servers[i]);
                item.Default = item.Server == list.DefaultServer;
                item.Selected = item.Server == mBroadcastController.IngestServer;
                mIngestListListbox.Items.Add(item);
            }
        }

        #endregion

        #endregion

        #region Chat

        private void mChatInitializeButton_Click(object sender, EventArgs e)
        {
            mChatController.ClientId = mClientIdText.Text;
            mChatController.EmoticonParsingMode = (Twitch.Chat.ChatController.EmoticonMode)mEmoticonModeCombobox.SelectedItem;
            mChatController.Initialize();
        }

        private void mChatShutdownButton_Click(object sender, EventArgs e)
        {
            mChatController.Shutdown();
        }

        private void mChatConnectButton_Click(object sender, EventArgs e)
        {
            mChatController.UserName = mBroadcastController.UserName;
            mChatController.AuthToken = mBroadcastController.AuthToken;

            if (mChatController.Connect(mChatChannelText.Text))
            {
                mChatStateLabel.Text = "Connecting";
            }
        }

        private void mChatDisconnectButton_Click(object sender, EventArgs e)
        {
            mChatController.Disconnect(mChatChannelText.Text);
        }

        private void mChatSendButton_Click(object sender, EventArgs e)
        {
            mChatController.SendChatMessage(mChatChannelText.Text, mChatInputTextbox.Text);
            mChatInputTextbox.Text = "";
        }

        private void mChatTimer_Tick(object sender, EventArgs e)
        {
            mChatController.Update();

            mChatStatusLabel.Text = mChatController.CurrentState.ToString();
        }

        private void mChatUsersListbox_MouseDown(object sender, MouseEventArgs e)
        {
            Twitch.Chat.ChatUserInfo user = mChatUsersListbox.SelectedItem as Twitch.Chat.ChatUserInfo;
            if (user == null)
            {
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                mChatIgnoreMenuItem.Checked = user.IsIgnored;
                mChatBanMenuItem.Checked = user.IsBanned;
                mChatModeratorMenuItem.Checked = user.IsModerator;

                mChatUserContextMenu.Tag = mChatUsersListbox.SelectedItem;
                mChatUserContextMenu.Show(this, new Point(mChatUsersListbox.Left + e.X, mChatUsersListbox.Top + e.Y));
            }
        }

        private void banToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (mChatUserContextMenu.Tag == null)
            {
                return;
            }

            Twitch.Chat.ChatUserInfo user = mChatUserContextMenu.Tag as Twitch.Chat.ChatUserInfo;
            if (user == null)
            {
                return;
            }

            if (user.IsBanned)
            {
                mChatController.SendChatMessage(mChatChannelText.Text, "/unban " + user.DisplayName);
            }
            else
            {
                mChatController.SendChatMessage(mChatChannelText.Text, "/ban " + user.DisplayName);
            }
        }

        private void moderatorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (mChatUserContextMenu.Tag == null)
            {
                return;
            }

            Twitch.Chat.ChatUserInfo user = mChatUserContextMenu.Tag as Twitch.Chat.ChatUserInfo;
            if (user == null)
            {
                return;
            }

            if (user.IsModerator)
            {
                mChatController.SendChatMessage(mChatChannelText.Text, "/unmod " + user.DisplayName);
            }
            else
            {
                mChatController.SendChatMessage(mChatChannelText.Text, "/mod " + user.DisplayName);
            }
        }

        private void ignoreToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (mChatUserContextMenu.Tag == null)
            {
                return;
            }

            Twitch.Chat.ChatUserInfo user = mChatUserContextMenu.Tag as Twitch.Chat.ChatUserInfo;
            if (user == null)
            {
                return;
            }

            if (user.IsIgnored)
            {
                mChatController.SendChatMessage(mChatChannelText.Text, "/unignore " + user.DisplayName);
            }
            else
            {
                mChatController.SendChatMessage(mChatChannelText.Text, "/ignore " + user.DisplayName);
            }
        }

        #region Callbacks
        
        protected void HandleChatConnected(string channelName)
        {
            mChatStateLabel.Text = "Connected";

            mChatMessagesGroupbox.Enabled = true;
        }

        protected void HandleChatDisconnected(string channelName)
        {
            mChatStateLabel.Text = "Disconnected";
            mChatUsersListbox.Items.Clear();
            mChatMessagesTextbox.Clear();
            mChatMessagesGroupbox.Enabled = false;
        }

        protected void HandleRawMessagesReceived(string channelName, Twitch.Chat.ChatRawMessage[] messages)
        {
            for (int i = 0; i < messages.Length; ++i)
            {
                string line = messages[i].UserName + ": " + messages[i].Message + "\r\n";
                mChatMessagesTextbox.Text = mChatMessagesTextbox.Text + line;
            }
        }

        protected void HandleTokenizedMessagesReceived(string channelName, Twitch.Chat.ChatTokenizedMessage[] messages)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < messages.Length; ++i)
            {
                Twitch.Chat.ChatTokenizedMessage msg = messages[i];
                sb.Append(msg.DisplayName).Append(": ");

                for (int t = 0; t < msg.Tokens.Length; ++t)
                {
                    Twitch.Chat.ChatMessageToken token = msg.Tokens[t];
                    switch (token.Type)
                    {
                        case Twitch.Chat.TTV_ChatMessageTokenType.TTV_CHAT_MSGTOKEN_TEXT:
                        {
                            Twitch.Chat.ChatTextMessageToken mt = token as Twitch.Chat.ChatTextMessageToken;
                            sb.Append(mt.Message);
                            break;
                        }
                        case Twitch.Chat.TTV_ChatMessageTokenType.TTV_CHAT_MSGTOKEN_TEXTURE_IMAGE:
                        {
                            Twitch.Chat.ChatTextureImageMessageToken mt = token as Twitch.Chat.ChatTextureImageMessageToken;
                            sb.Append(string.Format("[{0},{1},{2},{3},{4}]", mt.SheetIndex, mt.X1, mt.Y1, mt.X2, mt.Y2));
                            break;
                        }
                        case Twitch.Chat.TTV_ChatMessageTokenType.TTV_CHAT_MSGTOKEN_URL_IMAGE:
                        {
                            Twitch.Chat.ChatUrlImageMessageToken mt = token as Twitch.Chat.ChatUrlImageMessageToken;
                            sb.Append("[").Append(mt.Url).Append("]");
                            break;
                        }
                    }
                }
                sb.AppendLine();

                mChatMessagesTextbox.Text = mChatMessagesTextbox.Text + sb.ToString();
                sb.Clear();
            }
        }

        protected void HandleUsersChanged(string channelName, Twitch.Chat.ChatUserInfo[] joins, Twitch.Chat.ChatUserInfo[] leaves, Twitch.Chat.ChatUserInfo[] infoChanges)
        {
            for (int i = 0; i < leaves.Length; ++i)
            {
                mChatUsersListbox.Items.Remove(leaves[i]);
            }

            for (int i = 0; i < infoChanges.Length; ++i)
            {
                mChatUsersListbox.Items.Remove(infoChanges[i]);
                mChatUsersListbox.Items.Add(infoChanges[i]);
            }

            for (int i = 0; i < joins.Length; ++i)
            {
                mChatUsersListbox.Items.Add(joins[i]);
            }
        }

        protected void HandleChatStateChanged(Twitch.Chat.WinFormsChatController.ChatState state)
        {
            mChatStateLabel.Text = state.ToString();

            switch (state)
            {
                case Twitch.Chat.ChatController.ChatState.Uninitialized:
                    break;
                case Twitch.Chat.ChatController.ChatState.Initializing:
                    break;
                case Twitch.Chat.ChatController.ChatState.Initialized:
                    break;
                case Twitch.Chat.ChatController.ChatState.ShuttingDown:
                    break;
            }
        }

        protected void HandleClearMessages(string channelName, string username)
        {
            mChatMessagesTextbox.Text = "";

            // NOTE: This is not a very optimized way of doing this.  All messages are re-added because
            // we're using a primitive text box to display messages.

            if (mChatController.EmoticonParsingMode == Twitch.Chat.ChatController.EmoticonMode.None)
            {
                LinkedList<Twitch.Chat.ChatRawMessage>.Enumerator e = mChatController.GetRawMessages(channelName);

                while (e.MoveNext())
                {
                    Twitch.Chat.ChatRawMessage msg = e.Current;
                    HandleRawMessagesReceived(channelName, new Twitch.Chat.ChatRawMessage[] { msg });
                }
            }
            else
            {
                LinkedList<Twitch.Chat.ChatTokenizedMessage>.Enumerator e = mChatController.GetTokenizedMessages(channelName);

                while (e.MoveNext())
                {
                    Twitch.Chat.ChatTokenizedMessage msg = e.Current;
                    HandleTokenizedMessagesReceived(channelName, new Twitch.Chat.ChatTokenizedMessage[] { msg });
                }
            }
        }

        protected void HandleEmoticonDataAvailable()
        {
        }

        protected void HandleEmoticonDataExpired()
        {
        }

        protected void HandleBadgeDataAvailable(string channelName)
        {
        }

        protected void HandleBadgeDataExpired(string channelName)
        {
        }

        #endregion

        #endregion
    }
}
