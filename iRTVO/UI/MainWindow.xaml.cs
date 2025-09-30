﻿/* 
 * MainWindow.xaml.cs
 * 
 * Functionality of the MainWindow (program controls)
 * 
 */
using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;

// additional
using System.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Threading;
using System.Windows.Interop;
using System.IO;
using NLog;
using iRTVO.Networking;
using iRTVO.Interfaces;
using iRTVO.Data;
using System.Reflection;

namespace iRTVO
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        // Create overlay
        Window overlayWindow;

        // Create controls
        Window controlsWindow;

        // Create lists
        Window listsWindow;

        // update timer
        DispatcherTimer updateTimer = new DispatcherTimer();

        // trigger timer
        DispatcherTimer triggerTimer = new DispatcherTimer();

        // custom buttons
        StackPanel[] userButtonsRow;
        Button[] buttons;
        HotKey[] hotkeys;

        // web update wait
        Int16 webUpdateWait = 0;

        // API
        ISimulationAPI simulationAPI;

        // Logging
        private static Logger logger = LogManager.GetCurrentClassLogger();

        // Buffer for remote switches
        int RemoteCamNum = -1;
        int RemoteDriverNum = -1;

        public MainWindow()
        {
            logger.Info("iRTVO starting");
            InitializeComponent();


            // set window position
            this.Left = SharedData.settings.MainWindowLocationX > 0 ? SharedData.settings.MainWindowLocationX : 0;
            this.Top = SharedData.settings.MainWindowLocationY > 0 ? SharedData.settings.MainWindowLocationY : 0;
            this.Width = SharedData.settings.MainWindowWidth;
            this.Height = SharedData.settings.MainWindowHeight;

            if (SharedData.settings.AlwaysOnTopMainWindow)
                this.Topmost = true;
            else
                this.Topmost = false;


            simulationAPI = new iRacingAPI();

            overlayWindow = new Overlay(simulationAPI);
            controlsWindow = new Controls(simulationAPI);


        }

        private int NextConnectTry = Environment.TickCount;
        private Thread simulationThread = null;

        private void connectApis(object sender, EventArgs e)
        {
            if (!simulationAPI.IsConnected)
            {
                if (Environment.TickCount > NextConnectTry)
                {
                    logger.Trace("Trying to connect to Simulation...");
                    if (!simulationAPI.ConnectAPI())
                    {
                        logger.Warn("Could not connect to simulation. Deferring retry...");
                    }
                    else
                        logger.Info("Connected to simulation.");
                    NextConnectTry = Environment.TickCount + SharedData.settings.SimulationConnectDelay * 1000;
                }
            }
            else
                if ((simulationThread == null) || !simulationThread.IsAlive)
                {
                    simulationThread = new Thread(new ThreadStart(UpdateSimulationData));
                    simulationThread.IsBackground = true;
                    simulationThread.Start();
                }
        }

        private void UpdateSimulationData()
        {
            while (SharedData.runApi)
            {
                if (!simulationAPI.IsConnected)
                    return;
                if (!simulationAPI.UpdateAPIData())
                    return;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            overlayWindow.Show();
            controlsWindow.Show();

            // start timers
            updateTimer.Tick += new EventHandler(connectApis);
            updateTimer.Tick += new EventHandler(updateStatusBar);
            updateTimer.Tick += new EventHandler(updateButtons);
            updateTimer.Tick += new EventHandler(checkWebUpdate);
            updateTimer.Tick += new EventHandler(pageSwitcher);
            updateTimer.Interval = new TimeSpan(0, 0, 0, 1, 0);
            updateTimer.Start();

            // trigger timer runs same speed as the overlay
            int updateMs = (int)Math.Round(1000 / (double)SharedData.settings.UpdateFPS);
            triggerTimer.Tick += new EventHandler(triggerTimer_Tick);
            triggerTimer.Interval = new TimeSpan(0, 0, 0, 0, updateMs);
            triggerTimer.Start();

            iRTVOConnection.ProcessMessage += iRTVOConnection_ProcessMessage;

            // autostart client/server
            if (SharedData.settings.RemoteControlClientAutostart)
            {
                Button dummyButton = new Button();
                this.bClient_Click(dummyButton, new RoutedEventArgs());
            }
            else
                if (SharedData.settings.RemoteControlServerAutostart)
                {
                    Button dummyButton = new Button();
                    this.bServer_Click(dummyButton, new RoutedEventArgs());
                }
            this.Title = Assembly.GetEntryAssembly().AssemblyTitle();
            LoadExtensions();
        }

        // trigger handler
        void triggerTimer_Tick(object sender, EventArgs e)
        {
            if (!SharedData.runOverlay)
                return;
            TriggerTypes trigger;
            object triggerObject;
            while (SharedData.triggers.TryPop(out triggerObject))
            {
                if (triggerObject is TriggerInfo)
                {
                    TriggerInfo t = triggerObject as TriggerInfo;
                    SharedData.currentTriggerCarIdx = t.CarIdx;
                    trigger = t.Trigger;
                }
                else
                    trigger = (TriggerTypes)triggerObject;
                int triggerId = -1;
                logger.Info("Trigger: Processing {0}", trigger);
                // search matching trigger and pick first
                for (int i = 0; i < SharedData.theme.triggers.Length; i++)
                {
                    if (SharedData.theme.triggers[i].name.ToLower() == trigger.ToString().ToLower())
                    {
                        triggerId = i;
                        break;
                    }
                }

                // if trigger found execute it
                if (triggerId >= 0)
                {
                    logger.Debug("Trigger: Executing '{0}' for {1}", SharedData.theme.triggers[triggerId].name, trigger);
                    for (int i = 0; i < SharedData.theme.triggers[triggerId].actions.Length; i++)
                    {
                        Theme.ButtonActions action = (Theme.ButtonActions)i;
                        if (SharedData.theme.triggers[triggerId].actions[i] != null)
                        {
                            ClickAction(action, SharedData.theme.triggers[triggerId].actions[i]);
                        }
                    }
                }
            }
        }

        // no focus
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (SharedData.settings.LoseFocus)
            {
                //Set the window style to noactivate.
                WindowInteropHelper helper = new WindowInteropHelper(this);
                SetWindowLong(helper.Handle, GWL_EXSTYLE, GetWindowLong(helper.Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
            }
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private void updateButtons(object sender, EventArgs e)
        {
            if (SharedData.refreshButtons == true)
            {
                int CamRowStart = 0;

                buttonStackPanel.Children.RemoveRange(1, buttonStackPanel.Children.Count - 1);

                int rowCount = 0;
                for (int i = 0; i < SharedData.theme.buttons.Length; i++)
                {
                    if (SharedData.theme.buttons[i].row > rowCount)
                        rowCount = SharedData.theme.buttons[i].row;
                }

                if (SharedData.settings.CamerasButtonColumn)
                {
                    int numCamRows = (SharedData.Camera.Groups.Count / SharedData.settings.CamerasPerColumn) + 1;
                    CamRowStart = rowCount + 1;
                    rowCount += numCamRows;
                }
                userButtonsRow = new StackPanel[rowCount + 1];

                for (int i = 0; i < userButtonsRow.Length; i++)
                {
                    userButtonsRow[i] = new StackPanel();
                    buttonStackPanel.Children.Add(userButtonsRow[i]);
                }

                if (hotkeys == null)
                    hotkeys = new HotKey[SharedData.theme.buttons.Length];

                buttons = new Button[SharedData.theme.buttons.Length];
                for (int i = 0; i < SharedData.theme.buttons.Length; i++)
                {
                    buttons[i] = new Button();
                    buttons[i].Content = SharedData.theme.buttons[i].text;
                    buttons[i].Click += new RoutedEventHandler(HandleClick);
                    buttons[i].Name = "customButton" + i.ToString();
                    buttons[i].Margin = new Thickness(3);
                    if (!SharedData.theme.buttons[i].hidden)
                    {
                        userButtonsRow[SharedData.theme.buttons[i].row].Children.Add(buttons[i]);
                    }
                    // hotkeys
                    if (SharedData.theme.buttons[i].hotkey.key != Key.None && hotkeys[i] == null)
                    {
                        hotkeys[i] = new HotKey(SharedData.theme.buttons[i].hotkey.key, SharedData.theme.buttons[i].hotkey.modifier, HotKeyHandler);
                    }
                }

                if (SharedData.settings.CamerasButtonColumn)
                {
                    int ct = 0;
                    int curRow = CamRowStart;
                    foreach (CameraGroup cam in SharedData.Camera.Groups)
                    {
                        if (SharedData.settings.IgnoredCameras.Contains(cam.Name.ToUpper()))
                            continue;
                        logger.Info("Adding cam {0}", cam.Name);
                        Button button = new Button();
                        button.Content = cam.Name;
                        button.Name = "cameraButton" + cam.Id;
                        button.Margin = new Thickness(3);
                        button.Click += new RoutedEventHandler(CameraButtonClick);

                        userButtonsRow[curRow].Children.Add(button);
                        ct++;
                        if ((ct % SharedData.settings.CamerasPerColumn) == 0)
                            curRow++;
                    }

                }
                SharedData.refreshButtons = false;
            }

            int sessions = 0;

            for (int i = 0; i < SharedData.Sessions.SessionList.Count; i++)
            {
                if (SharedData.Sessions.SessionList[i].Type != SessionTypes.none)
                    sessions++;
            }

            ComboBoxItem cboxitem;
            string selected = null;

            if (comboBoxSession.HasItems)
            {
                cboxitem = (ComboBoxItem)comboBoxSession.SelectedItem;
                selected = cboxitem.Content.ToString();
            }

            if (comboBoxSession.Items.Count != (sessions + 1))
            {
                comboBoxSession.Items.Clear();
                cboxitem = new ComboBoxItem();
                cboxitem.Content = "current";
                comboBoxSession.Items.Add(cboxitem);

                for (int i = 0; i < SharedData.Sessions.SessionList.Count; i++)
                {
                    if (SharedData.Sessions.SessionList[i].Type != SessionTypes.none)
                    {
                        cboxitem = new ComboBoxItem();
                        cboxitem.Content = i.ToString() + ": " + SharedData.Sessions.SessionList[i].Type.ToString();
                        comboBoxSession.Items.Add(cboxitem);
                    }
                }

                if (selected != null)
                    comboBoxSession.Text = selected;
                else
                    comboBoxSession.Text = "current";
            }


            // update Server / Client buttons
            if (!iRTVOConnection.isServer)
            {
                if (iRTVOConnection.isConnected)
                {
                    bClient.Content = "[connected]";
                    bClientFollow.IsEnabled = true;
                }
                else
                {
                    bClient.Content = "Connect client";
                    bClientFollow.IsEnabled = false;
                }
            }
            else
                bClientFollow.IsEnabled = false;
        }

        private void pageSwitcher(object sender, EventArgs e)
        {
            if (SharedData.theme == null)
                return;
            for (int i = 0; i < SharedData.theme.buttons.Length; i++)
            {
                if (SharedData.theme.buttons[i].active == true &&
                    (SharedData.theme.buttons[i].delay > 0) &&
                   (DateTime.Now - SharedData.theme.buttons[i].pressed).TotalSeconds >= SharedData.theme.buttons[i].delay)
                {
                    Button dummyButton = new Button();
                    dummyButton.Name = "customButton" + i.ToString();
                    dummyButton.Content = "";
                    this.HandleClick(dummyButton, new RoutedEventArgs());
                }
            }
        }

        void HandleClick(object sender, RoutedEventArgs e)
        {
            Button button = new Button();
            if (sender is Button)
                button = sender as Button;

            int buttonId = Int32.Parse(button.Name.Substring(12));

            if ((button.Content != null && button.Content.ToString() != "") && (e.OriginalSource is Button))
            {
                // Broadcast Click to all clients or the server
                iRTVOConnection.BroadcastMessage("BUTTON", button.Name);
                if (iRTVOConnection.isConnected && !iRTVOConnection.isServer)
                    return;
            }

            // All Buttons keep track if they are in active state
            if (SharedData.theme.buttons[buttonId].active && button.Content.ToString() != "")
                SharedData.theme.buttons[buttonId].active = false;
            else
            {
                SharedData.theme.buttons[buttonId].pressed = DateTime.Now;
                SharedData.theme.buttons[buttonId].active = true;
            }


            for (int i = 0; i < SharedData.theme.buttons[buttonId].actions.Length; i++)
            {
                Theme.ButtonActions action = (Theme.ButtonActions)i;
                if (SharedData.theme.buttons[buttonId].actions[i] != null)
                {
                    if (ClickAction(action, SharedData.theme.buttons[buttonId].actions[i])) // if last page
                    {
                        if (SharedData.theme.buttons[buttonId].delayLoop) // keep pushing
                        {
                            ClickAction(action, SharedData.theme.buttons[buttonId].actions[i]);
                        }
                        else // hide
                        {
                            if (SharedData.theme.buttons[buttonId].active)
                            {
                                if (action != Theme.ButtonActions.toggle)
                                    ClickAction(Theme.ButtonActions.hide, SharedData.theme.buttons[buttonId].actions[i]);
                                SharedData.theme.buttons[buttonId].active = false;
                            }
                        }
                    }

                    if (SharedData.theme.buttons[buttonId].delayLoop && !SharedData.theme.buttons[buttonId].active)
                    {
                        ClickAction(Theme.ButtonActions.hide, SharedData.theme.buttons[buttonId].actions[i]);
                    }
                }
            }
        }

        void CameraButtonClick(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null)
                return;
            ClickAction(Theme.ButtonActions.camera, new string[] { Convert.ToString(btn.Content) });

        }

        private Boolean ClickAction(Theme.ButtonActions action, string[] objects)
        {
            return ClickAction(action, objects, false);
        }

        private Boolean ClickAction(Theme.ButtonActions action, string[] objects, bool noLoop)
        {
            logger.Trace("ClickAction: Action {0} objects {1}", action, String.Join(" , ", objects));
            bool retVal = false;
            for (int j = 0; j < objects.Length; j++)
            {
                string[] split = objects[j].Split('-');
                switch (action)
                {
                    case Theme.ButtonActions.camera:
                        int camera = -1;
                        foreach (CameraGroup cam in SharedData.Camera.Groups)
                        {
                            if (cam.Name.ToLower() == objects[j].ToLower())
                                camera = cam.Id;
                        }
                        if (camera >= 0)
                        {
                            int driver = Controls.padCarNum(SharedData.Sessions.CurrentSession.FollowedDriver.Driver.NumberPlate);
                            simulationAPI.SwitchCamera(driver, camera);

                            iRTVOConnection.BroadcastMessage("SWITCH", driver, camera);

                            SharedData.updateControls = true;
                        }
                        break;
                    case Theme.ButtonActions.replay:
                        int replay = -1;
                        bool result = Int32.TryParse(objects[j], out replay);
                        if (result)
                        {
                            if (replay == 0) // live
                            {
                                Thread.Sleep(16);
                                simulationAPI.ReplaySearch(ReplaySearchModeTypes.ToEnd, 0);
                                simulationAPI.Play();
                                SharedData.updateControls = true;
                                SharedData.triggers.Push(TriggerTypes.live);

                                iRTVOConnection.BroadcastMessage("LIVE");
                            }
                            else // replay
                            {
                                Thread.Sleep(16);
                                int numFrames = (int)((Int32)simulationAPI.GetData("ReplayFrameNum") - (replay * 60));
                                simulationAPI.ReplaySetPlayPosition(ReplayPositionModeTypes.Begin, numFrames);
                                simulationAPI.Play();
                                SharedData.triggers.Push(TriggerTypes.replay);

                                iRTVOConnection.BroadcastMessage("REWIND", numFrames, 1);

                            }
                        }
                        break;
                    case Theme.ButtonActions.playspeed:
                        Int32 playspeed = Int32.Parse(objects[j]);
                        Int32 slowmo = 0;
                        if (playspeed < 0)
                        {
                            playspeed = Math.Abs(playspeed) - 1;
                            slowmo = 1;
                        }

                        Thread.Sleep(16);
                        simulationAPI.ReplaySetPlaySpeed(playspeed, slowmo);
                        break;
                    default:
                        switch (split[0])
                        {
                            case "Overlay": // overlays
                                for (int k = 0; k < SharedData.theme.objects.Length; k++)
                                {
                                    if (SharedData.theme.objects[k].name == split[1])
                                    {
                                        if (noLoop)
                                        {
                                            SharedData.theme.objects[k].visible = setObjectVisibility(SharedData.theme.objects[k].visible, action);
                                            return false;
                                        }
                                        Boolean isStandings = SharedData.theme.objects[k].dataset == DataSets.standing || SharedData.theme.objects[k].dataset == DataSets.points;

                                        if ((action == Theme.ButtonActions.show) || (action == Theme.ButtonActions.toggle && SharedData.theme.objects[k].visible == false))
                                        {
                                            if (isStandings)
                                                SharedData.theme.objects[k].page++;
                                            else
                                                SharedData.theme.objects[k].page = 0;
                                        }

                                        if (SharedData.lastPage[k] == true && isStandings && action == Theme.ButtonActions.show)
                                        {
                                            //SharedData.theme.objects[k].visible = setObjectVisibility(SharedData.theme.objects[k].visible, Theme.ButtonActions.hide);
                                            ClickAction(Theme.ButtonActions.hide, objects, true);
                                            action = Theme.ButtonActions.hide;
                                            SharedData.theme.objects[k].page = -1;
                                            SharedData.lastPage[k] = false;

                                            retVal = true;
                                        }
                                        else
                                        {

                                            SharedData.theme.objects[k].visible = setObjectVisibility(SharedData.theme.objects[k].visible, action);
                                            if ((action == Theme.ButtonActions.toggle) && (SharedData.theme.objects[k].visible == false))
                                                retVal = true;
                                        }
                                        logger.Trace("Overlay-{0} vis is now {1} act {2}", split[1], SharedData.theme.objects[k].visible, action);
                                    }
                                }
                                break;
                            case "Image": // images
                                for (int k = 0; k < SharedData.theme.images.Length; k++)
                                {
                                    if (SharedData.theme.images[k].name == split[1])
                                    {
                                        SharedData.theme.images[k].visible = setObjectVisibility(SharedData.theme.images[k].visible, action);
                                        logger.Trace("Image-{0} vis is now {1} act {2}", split[1], SharedData.theme.images[k].visible, action);
                                    }
                                }
                                break;
                            case "Ticker": // tickers
                                for (int k = 0; k < SharedData.theme.tickers.Length; k++)
                                {
                                    if (SharedData.theme.tickers[k].name == split[1])
                                    {
                                        SharedData.theme.tickers[k].visible = setObjectVisibility(SharedData.theme.tickers[k].visible, action);
                                    }
                                }
                                break;
                            case "Video": // video
                                for (int k = 0; k < SharedData.theme.videos.Length; k++)
                                {
                                    if (SharedData.theme.videos[k].name == split[1])
                                    {
                                        SharedData.theme.videos[k].visible = setObjectVisibility(SharedData.theme.videos[k].visible, action);
                                    }
                                }
                                break;
                            case "Sound": // sound
                                for (int k = 0; k < SharedData.theme.sounds.Length; k++)
                                {
                                    if (SharedData.theme.sounds[k].name == split[1])
                                    {
                                        SharedData.theme.sounds[k].playing = setObjectVisibility(SharedData.theme.sounds[k].playing, action);
                                    }
                                }
                                break;
                            case "Trigger": // triggers
                                switch (split[1])
                                {
                                    case "flags":
                                        if (SharedData.Sessions.CurrentSession.Flag == SessionFlags.white)
                                            SharedData.triggers.Push(TriggerTypes.flagWhite);
                                        else if (SharedData.Sessions.CurrentSession.Flag == SessionFlags.checkered)
                                            SharedData.triggers.Push(TriggerTypes.flagCheckered);
                                        else if (SharedData.Sessions.CurrentSession.Flag == SessionFlags.yellow)
                                            SharedData.triggers.Push(TriggerTypes.flagYellow);
                                        else
                                            SharedData.triggers.Push(TriggerTypes.flagGreen);
                                        break;
                                    case "lights":
                                        if (SharedData.Sessions.CurrentSession.StartLight == SessionStartLights.ready)
                                            SharedData.triggers.Push(TriggerTypes.lightsReady);
                                        else if (SharedData.Sessions.CurrentSession.StartLight == SessionStartLights.set)
                                            SharedData.triggers.Push(TriggerTypes.lightsSet);
                                        else if (SharedData.Sessions.CurrentSession.StartLight == SessionStartLights.go)
                                            SharedData.triggers.Push(TriggerTypes.lightsGo);
                                        else
                                            SharedData.triggers.Push(TriggerTypes.lightsOff);
                                        break;
                                    default:
                                        break;
                                }
                                break;
                            case "Click":
                                {
                                    for (int i = 0; i < SharedData.theme.buttons.Length; i++)
                                    {
                                        if (SharedData.theme.buttons[i].name == split[1])
                                        {
                                            Button dummyButton = new Button();
                                            dummyButton.Name = "customButton" + i.ToString();
                                            dummyButton.Content = "";
                                            this.HandleClick(dummyButton, new RoutedEventArgs());
                                            break;
                                        }
                                    }
                                    break;
                                }
                            case "Push":
                                {
                                    TriggerTypes x = (TriggerTypes)Enum.Parse(typeof(TriggerTypes), split[1], true);
                                    SharedData.triggers.Push(x);

                                    break;
                                }
                            default: // script or not
                                if (SharedData.scripting.Scripts.Contains(split[0]))
                                {
                                    SharedData.scripting.PressButton(split[0], split[1]);
                                }
                                break;
                        }
                        break;
                }
            }
            return retVal;
        }

        private Boolean setObjectVisibility(Boolean currentValue, Theme.ButtonActions action)
        {
            if (action == Theme.ButtonActions.hide)
                return false;
            else if (action == Theme.ButtonActions.show)
                return true;
            else if (action == Theme.ButtonActions.toggle)
            {
                if (currentValue == true)
                    return false;
                else
                    return true;
            }
            else
                return true;
        }

        private string formatBytes(Int64 bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = 0;
            for (i = 0; (int)(bytes / 1024) > 0; i++, bytes /= 1024)
                dblSByte = bytes / 1024.0;
            return String.Format("{0:0.00} {1}", dblSByte, Suffix[i]);
        }

        private void updateStatusBar(object sender, EventArgs e)
        {
            if (SharedData.apiConnected)
            {
                if (!overlayWindow.IsVisible)
                    overlayWindow.Show();
                statusBarState.Text = "Sim: Running";
            }
            else
            {
                statusBarState.Text = "Sim: No connection";
            }

            statusBarFps.Text = SharedData.overlayUpdateTime.ToString() + " ms";

            if (SharedData.settings.WebTimingEnable &&
                (SharedData.Sessions.CurrentSession.State != SessionStates.invalid) &&
                SharedData.runOverlay)
            {
                statusBarWebTiming.Text = "Web: enabled";

                Brush textColor = System.Windows.SystemColors.WindowTextBrush;
                if (SharedData.webError.Length > 0)
                    textColor = System.Windows.Media.Brushes.Red;

                statusBarWebTiming.Foreground = textColor;
            }
            else
            {
                statusBarWebTiming.Text = "Web: disabled";
            }

            if (SharedData.webError.Length > 0)
                statusBarWebTiming.ToolTip = string.Format("Error: {0}", SharedData.webError);
            else
                statusBarWebTiming.ToolTip = string.Format("Out: {0}", formatBytes(SharedData.webBytes));

            if (comboBoxSession.SelectedItem != null)
            {
                ComboBoxItem cbi = (ComboBoxItem)comboBoxSession.SelectedItem;
                if (cbi.Content.ToString() == "current")
                {
                    SharedData.OverlaySession = SharedData.Sessions.CurrentSession.Id;
                }
            }

            statusBarFps.ToolTip = "";

            if (SharedData.cacheMiss > 0 && SharedData.cacheHit > 0 && SharedData.cacheFrameCount > 0)
            {
                statusBarFps.ToolTip = statusBarFps.ToolTip + string.Format("\ncache hitrate: {0:00}% for {1} objects", ((float)SharedData.cacheHit / ((float)SharedData.cacheMiss + (float)SharedData.cacheHit)) * 100, (SharedData.cacheMiss + SharedData.cacheHit) / SharedData.cacheFrameCount);
                SharedData.cacheMiss = 0;
                SharedData.cacheHit = 0;
                SharedData.cacheFrameCount = 0;
            }

            if (App.ErrorOccoured)
            {
                errorInformation.Text = "Caught error: " + App.LastError;
            }
        }

        private void checkWebUpdate(object sender, EventArgs e)
        {
            if (SharedData.settings.WebTimingEnable &&
                (SharedData.Sessions.CurrentSession.State != SessionStates.invalid) &&
                SharedData.runOverlay &&
                webUpdateWait > SharedData.settings.WebTimingUpdateInterval)
            {
                ThreadPool.QueueUserWorkItem(SharedData.web.postData);
                webUpdateWait = 0;
            }
            else
            {
                webUpdateWait++;
            }
        }

        private void CloseProgram()
        {
            logger.Info("iRTVO Closing");
            SharedData.runApi = false;

            if (overlayWindow != null)
                overlayWindow.Close();
            if (controlsWindow != null)
                controlsWindow.Close();
            if (listsWindow != null)
                listsWindow.Close();

            foreach (var ext in allExtensions)
            {
                try
                {
                    logger.Info("Shutting down {0}", ext.ToString());
                    ext.ShutdownExtension();
                }
                catch (Exception ex)
                {

                    logger.Fatal("Error shutting down extension {0}: {1}", ext.ToString(), ex.ToString());
                }
            }
            // Shutdown all network threads
            iRTVOConnection.Shutdown();

            string[] args = Environment.CommandLine.Split(' ');
            if (args.Length > 2 && args[args.Length - 1] == "-debug")
                SharedData.writeCache(SharedData.Sessions.SessionId);

            Thread.Sleep(1000); // Give Background Threads enough time to stop
            if (SharedData.theme != null)
                logger.Debug(SharedData.theme.DynamicBrushCache.Statistics);

            Application.Current.Shutdown(0);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            CloseProgram();
        }

        private void hideButton_Click(object sender, RoutedEventArgs e)
        {
            // skipping sending if the sender is dummy button, i.e. this was received command ands shouldn't be sent back
            Button button = new Button();
            try
            {
                button = (Button)sender;
            }
            finally
            {
                if ((button.Content != null && button.Content.ToString() != "") && (e.OriginalSource is Button))
                {
                    iRTVOConnection.BroadcastMessage("HIDE");
                }
            }
            if (!SharedData.runOverlay)
                return;
            for (int i = 0; i < SharedData.theme.buttons.Length; i++)
                SharedData.theme.buttons[i].active = false;

            for (int i = 0; i < SharedData.theme.objects.Length; i++)
                SharedData.theme.objects[i].visible = false;

            for (int i = 0; i < SharedData.theme.images.Length; i++)
                SharedData.theme.images[i].visible = false;

            for (int i = 0; i < SharedData.theme.tickers.Length; i++)
                SharedData.theme.tickers[i].visible = false;

            for (int i = 0; i < SharedData.theme.videos.Length; i++)
                SharedData.theme.videos[i].visible = false;

            for (int i = 0; i < SharedData.theme.sounds.Length; i++)
                SharedData.theme.sounds[i].playing = false;
        }

        private void Main_LocationChanged(object sender, EventArgs e)
        {
            SharedData.settings.MainWindowLocationX = (int)this.Left;
            SharedData.settings.MainWindowLocationY = (int)this.Top;
            SharedData.settings.Save();
        }

        private void Main_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SharedData.settings.MainWindowWidth = (int)this.Width;
            SharedData.settings.MainWindowHeight = (int)this.Height;
            SharedData.settings.Save();
        }

        private void comboBoxSession_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (comboBoxSession.Items.Count > 0)
            {
                ComboBoxItem cbi = (ComboBoxItem)comboBoxSession.SelectedItem;
                if (cbi.Content.ToString() == "current")
                {
                    SharedData.OverlaySession = SharedData.Sessions.CurrentSession.Id;
                }
                else
                {
                    string[] split = cbi.Content.ToString().Split(':');

                    SharedData.OverlaySession = Int32.Parse(split[0]);
                }
            }
        }


        List<IExtension> allExtensions = new List<IExtension>();

        public void LoadExtensions()
        {
            String path = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "ext");

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            foreach (var file in Directory.EnumerateFiles(path, "*.dll"))
            {
                try
                {
                    Assembly x = Assembly.LoadFile(file);
                    foreach (var t in x.GetExportedTypes())
                    {
                        if (t.GetInterfaces().Contains(typeof(IExtension)))
                        {
                            IExtension ex = Activator.CreateInstance(t) as IExtension;
                            allExtensions.Add(ex);
                            ex.InitializeExtension(simulationAPI, new SharedData_Public());
                            if (ex is IExtensionWindow)
                            {
                                IExtensionWindow exW = ex as IExtensionWindow;
                                Button b = new Button();
                                b.Content = exW.ButtonText;
                                b.Tag = exW;
                                b.Click += ExtensionsButton_Click;
                                defaultButtons.Children.Add(b);
                            }
                            logger.Info("Loaded Extension {0} from {1}", t.ToString(), file);
                        }
                    }

                }
                catch (Exception ex)
                {
                    logger.Error("Error loading {0}: {1}", file, ex.ToString());
                }

            }

        }

        void ExtensionsButton_Click(object sender, RoutedEventArgs e)
        {
            Button b = sender as Button;
            IExtensionWindow w = b.Tag as IExtensionWindow;
            w.ShowWindow();
        }

        private void bAbout_Click(object sender, RoutedEventArgs e)
        {

            Window about = new about();
            about.Show();
        }

        private void controlsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!controlsWindow.IsVisible)
            {
                controlsWindow = new Controls(simulationAPI);
                controlsWindow.Show();
            }
            controlsWindow.Activate();
        }

        private void listsButton_Click(object sender, RoutedEventArgs e)
        {
            if (listsWindow == null || !listsWindow.IsVisible)
            {
                listsWindow = new Lists();
                listsWindow.Show();
            }
            listsWindow.Activate();
        }



        private void bServer_Click(object sender, RoutedEventArgs e)
        {
            if (iRTVOConnection.isAvailable)
            {
                iRTVOConnection.NewClient += iRTVOConnection_NewClient;
                if (!iRTVOConnection.StartServer(SharedData.settings.RemoteControlServerPort, SharedData.settings.RemoteControlServerPassword))
                {
                    MessageBox.Show("Problem occurred trying to start the server", "Connect error");
                    return;
                }
                this.bClient.IsEnabled = false;
                this.bServer.IsEnabled = false;
            }

        }

        void iRTVOConnection_NewClient(string newClientID)
        {
            logger.Info("New Client {0} connected", newClientID);
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                // Sync shown overlay etc with new client
                foreach (var button in buttons)
                {
                    if (String.IsNullOrEmpty(Convert.ToString(button.Content)))
                        continue;
                    int buttonId = Int32.Parse(button.Name.Substring(12));
                    if (SharedData.theme.buttons[buttonId].active)
                        iRTVOConnection.SendMessage(newClientID, "BUTTON", button.Name);
                }
            }));
        }

        private void bClient_Click(object sender, RoutedEventArgs e)
        {
            if (iRTVOConnection.isAvailable || !iRTVOConnection.isConnected)
            {
                try
                {

                    if (!iRTVOConnection.StartClient(SharedData.settings.RemoteControlClientAddress, SharedData.settings.RemoteControlClientPort, SharedData.settings.RemoteControlClientPassword))
                    {
                        MessageBox.Show("Problem occurred trying to connect to the server", "Connect error");
                        return;
                    }
                    this.bServer.IsEnabled = false;
                }
                catch (Exception exc)
                {
                    MessageBox.Show(string.Format("Problem occurred trying to connect to the server: {0}", exc.Message), "Connect error");
                }
            }
            else
            {
                iRTVOConnection.Close();
                this.bServer.IsEnabled = true;
            }
        }

        private void RaiseThemeButtonEvent(string buttonName, string source)
        {
            // Find the Button            
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {

                var button = buttons.FirstOrDefault<Button>(x => String.Compare(x.Name, buttonName, true) == 0);
                if (button != null)
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, source));
            }));
        }

        private void SetPlaySpeed(int playspeed)
        {
            int slomo = 0;
            if (playspeed > 0)
                slomo = 1;
            else
            {
                playspeed = Math.Abs(playspeed);
            }
            simulationAPI.ReplaySetPlaySpeed(playspeed, slomo);
        }

        void iRTVOConnection_ProcessMessage(Networking.iRTVORemoteEvent e)
        {
            if (e.Handled)
                return;
            try
            {
                e.Handled = true;
                e.Forward = true; // by default Forward all events
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        switch (e.Message.Command.ToUpperInvariant())
                        {
                            case "CHGSESSION":
                                SharedData.OverlaySession = Int32.Parse(e.Message.Arguments[0]);

                                foreach (var item in comboBoxSession.Items)
                                {
                                    ComboBoxItem cbItem = item as ComboBoxItem;
                                    if (SharedData.OverlaySession == SharedData.Sessions.CurrentSession.Id)
                                    {
                                        if (cbItem.Content.ToString() == "current")
                                        {
                                            cbItem.IsSelected = true;
                                            break;
                                        }
                                    }
                                    else
                                    {
                                        if (cbItem.Content.ToString().StartsWith(e.Message.Arguments[0] + ":"))
                                        {
                                            cbItem.IsSelected = true;
                                            break;
                                        }
                                    }
                                }

                                break;
                            case "BUTTON":
                                RaiseThemeButtonEvent(e.Message.Arguments[0], e.Message.Source);
                                break;
                            case "RESET":
                                this.bReset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, e.Message.Source));
                                break;
                            case "HIDE":
                                this.hideButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, e.Message.Source));
                                break;
                            case "CAMERA":
                                e.Forward = false; // done by api code
                                RemoteCamNum = Int32.Parse(e.Message.Arguments[0]);
                                if (SharedData.remoteClientFollow || iRTVOConnection.isServer)
                                {
                                    simulationAPI.SwitchCamera(0, RemoteCamNum);
                                }

                                break;
                            case "DRIVER":
                                e.Forward = false; // done by api code
                                RemoteDriverNum = Int32.Parse(e.Message.Arguments[0]);
                                if (SharedData.remoteClientFollow || iRTVOConnection.isServer)
                                {
                                    simulationAPI.SwitchCamera(RemoteDriverNum, 0);
                                }
                                break;
                            case "SWITCH":
                                e.Forward = false; // done by api code
                                RemoteDriverNum = Int32.Parse(e.Message.Arguments[0]);
                                RemoteCamNum = Int32.Parse(e.Message.Arguments[1]);
                                if (SharedData.remoteClientFollow || iRTVOConnection.isServer)
                                {
                                    simulationAPI.SwitchCamera(RemoteDriverNum, RemoteCamNum);
                                    SharedData.updateControls = true;
                                }
                                break;
                            case "REWIND":
                                if (!SharedData.remoteClientSkipRewind)
                                {
                                    simulationAPI.ReplaySetPlayPosition(ReplayPositionModeTypes.Begin, ((Int32)simulationAPI.GetData("ReplayFrameNum") - Int32.Parse(e.Message.Arguments[0])));
                                    SetPlaySpeed(Int32.Parse(e.Message.Arguments[1]));
                                    SharedData.updateControls = true;
                                    SharedData.triggers.Push(TriggerTypes.replay);
                                }
                                break;
                            case "LIVE":
                                if (!SharedData.remoteClientSkipRewind)
                                {
                                    simulationAPI.ReplaySearch(ReplaySearchModeTypes.ToEnd, 0);
                                    simulationAPI.Play();
                                    SharedData.updateControls = true;
                                    SharedData.triggers.Push(TriggerTypes.live);
                                }
                                break;
                            case "PLAY":
                                if (!SharedData.remoteClientSkipRewind)
                                {
                                    simulationAPI.Play();
                                    SharedData.updateControls = true;
                                }
                                break;
                            case "PAUSE":
                                if (!SharedData.remoteClientSkipRewind)
                                {
                                    simulationAPI.Pause();
                                    SharedData.updateControls = true;
                                }
                                break;
                            case "PLAYSPEED":
                                if (!SharedData.remoteClientSkipRewind)
                                {
                                    simulationAPI.ReplaySetPlaySpeed(Int32.Parse(e.Message.Arguments[0]), Int32.Parse(e.Message.Arguments[1]));
                                    SharedData.updateControls = true;
                                }
                                break;
                            case "SENDCAMS":
                                e.Forward = false; // not needed by others
                                if (SharedData.Camera.Groups.Count > 0)
                                {
                                    foreach (CameraGroup cam in SharedData.Camera.Groups)
                                    {
                                        iRTVOConnection.SendMessage(e.Message.Source, "ADDCAM", cam.Id, cam.Name);
                                    }
                                }
                                break;
                            case "SENDDRIVERS":
                                e.Forward = false; // not needed by others
                                if (SharedData.Drivers.Count > 0)
                                {
                                    foreach (DriverInfo driver in SharedData.Drivers)
                                    {
                                        iRTVOConnection.SendMessage(e.Message.Source, "ADDDRIVER", driver.CarIdx, driver.Name);
                                    }
                                }
                                break;
                            case "SENDBUTTONS":
                                e.Forward = false; // not needed by others
                                foreach (var button in buttons)
                                {
                                    if (!String.IsNullOrEmpty(Convert.ToString(button.Content)))
                                        iRTVOConnection.SendMessage(e.Message.Source, "ADDBUTTON", button.Name, button.Content);
                                }
                                break;
                            default:
                                logger.Warn("Caught odd command: {0}", e.Message);
                                break;
                        }
                        SharedData.remoteClientSkipRewind = false;


                    }));
            }
            catch (Exception ex)
            {
                logger.Error("Problem executing command '{0}' : {1}", e.Message, ex.ToString());
                e.Cancel = true; // Disconnect offending Client/Server
            }
        }



        private void bReset_Click(object sender, RoutedEventArgs e)
        {
            string[] args = Environment.CommandLine.Split(' ');
            if (args.Length > 2 && args[args.Length - 1] == "-debug")
                SharedData.writeCache(SharedData.Sessions.SessionId);

            updateTimer.Stop();
            triggerTimer.Stop();

            SharedData.runApi = false;

            if (overlayWindow != null)
                overlayWindow.Close();
            if (controlsWindow != null)
                controlsWindow.Close();
            if (listsWindow != null)
                listsWindow.Close();

            SharedData.runApi = true;
            SharedData.runOverlay = false;
            SharedData.apiConnected = false;

            // Data
            SharedData.Drivers = new List<DriverInfo>();
            SharedData.Sessions = new Sessions();
            SharedData.Track = new TrackInfo();
            SharedData.Camera = new CameraInfo();
            SharedData.Events = new List<SessionEvent>();
            SharedData.Bookmarks = new Bookmarks();
            SharedData.Sectors = new List<Single>();
            SharedData.SelectedSectors = new List<Single>();
            SharedData.Classes = new Int32[3] { -1, -1, -1 };

            // Update stuff
            SharedData.updateControls = false;
            SharedData.showSimUi = true;

            overlayWindow = new Overlay(simulationAPI);
            controlsWindow = new Controls(simulationAPI);

            overlayWindow.Show();
            controlsWindow.Show();

            updateTimer.Start();
            triggerTimer.Start();


            Button senderButton = e.OriginalSource as Button;
            if (senderButton != null) // this was actually a click!
                iRTVO.Networking.iRTVOConnection.BroadcastMessage("RESET");

        }

        private void comboBoxClass_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (comboBoxClass.Items.Count > 0)
            {
                ComboBoxItem cbi = (ComboBoxItem)comboBoxClass.SelectedItem;
                if (cbi.Content.ToString() == "auto")
                    SharedData.overlayClass = null;
                else
                {
                    SharedData.overlayClass = cbi.Content.ToString();
                }
            }
        }

        private void bClientFollow_Click(object sender, RoutedEventArgs e)
        {
            if (SharedData.remoteClientFollow)
            {
                SharedData.remoteClientFollow = false;
                SharedData.remoteClientSkipRewind = true;
            }
            else
            {
                SharedData.remoteClientFollow = true;
                // Switch to what server sees after we have been "offline"
                simulationAPI.SwitchCamera(RemoteDriverNum, RemoteCamNum);
                SharedData.updateControls = true;
            }
        }

        private void bReload_Click(object sender, RoutedEventArgs e)
        {
            SharedData.refreshButtons = true;
            SharedData.refreshTheme = true;
        }

        public void HotKeyHandler(HotKey hotKey)
        {
            Console.WriteLine("Global hotkey " + hotKey.Key + " " + hotKey.KeyModifiers);
            for (int i = 0; i < hotkeys.Length; i++)
            {
                if (hotkeys[i] == hotKey)
                {
                    RaiseThemeButtonEvent("customButton" + i.ToString(), null);

                }

            }
        }


    }
}
