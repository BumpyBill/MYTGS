﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows;
using System.IO;
using System.Windows.Controls;
using Newtonsoft.Json;
using NLog;
using System.Windows.Media;

namespace MYTGS
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Logger logger = LogManager.GetCurrentClassLogger();
        //set to use only MYTGS firefly cloud 
        Firefly.Firefly FF { set; get; } = new Firefly.Firefly("MYTGS");
        Dictionary<int,Firefly.FullTask> Tasks = new Dictionary<int,Firefly.FullTask>();
        DispatcherTimer TenTimer = new DispatcherTimer();
        TimetableClock ClockWindow = new TimetableClock();
        

        public MainWindow()
        {
            // 10 minutes in milliseconds
            TenTimer.Interval = TimeSpan.FromMinutes(10);
            TenTimer.Tick += TenTimer_Tick;
            InitializeComponent(); //Initialize WPF Window and objects
            //test.Content = JsonConvert.SerializeObject(DateTime.Now.ToUniversalTime());
            ClockWindow.Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
            ClockWindow.Show();
            ClockWindow.Left = System.Windows.SystemParameters.WorkArea.Width - ClockWindow.Width;
            ClockWindow.Top = System.Windows.SystemParameters.WorkArea.Height - ClockWindow.Height;

            LoadCachedTasks();

            logger.Info("Beginning Login checks");
            FF.OnLogin += FF_OnLogin;
            if (!FF.OfflineMode && !FF.LoadKey())
            {
                FF.LoginUI();
            }
            TenTimer.Start();

            


        }

        private void LoadCachedTasks()
        {
            logger.Info("Loading Local tasks");
            string TasksPath = Environment.ExpandEnvironmentVariables((string)Properties.Settings.Default["TasksPath"]);
            if (Directory.Exists(TasksPath))
            {
                string[] TaskIDs = Directory.GetDirectories(TasksPath);
                TaskIDs.Reverse();
                int loadedtasks = 0;
                foreach (string TaskID in TaskIDs)
                {
                    try
                    {
                        if (File.Exists(TaskID + "\\Task.json"))
                        {
                            Firefly.FullTask tmp = JsonConvert.DeserializeObject<Firefly.FullTask>(File.ReadAllText(TaskID + "\\Task.json"));
                            Tasks.Add(tmp.id, tmp);
                            loadedtasks += 1;
                        }
                    }
                    catch
                    {
                        logger.Warn("Failed to load task - " + TaskID.Substring(TaskID.LastIndexOf("\\")));
                    }
                }
                logger.Info("Successfully loaded " + loadedtasks + " tasks");
            }
        }

        private void TenTimer_Tick(object sender, EventArgs e)
        {
            //Check for changes
            string TasksPath = Environment.ExpandEnvironmentVariables((string)Properties.Settings.Default["TasksPath"]);
            if (!Directory.Exists(TasksPath))
                Directory.CreateDirectory(TasksPath);
            if (TasksPath[TasksPath.Length - 1] != '\\' || TasksPath[TasksPath.Length - 1] != '/')
                TasksPath += "\\";
            UpdateTasks(TasksPath);
            
        }

        //Event fired when successfully connected to Firefly
        private void FF_OnLogin(object sender, EventArgs e)
        {
            logger.Info("Login successful!");
            

            StatusLabel.Dispatcher.Invoke(new Action(() => {
                StatusLabel.Content = "Welcome " + FF.Name;
            }));
            //TasksBlock.Text = "";
            string TasksPath = Environment.ExpandEnvironmentVariables((string)Properties.Settings.Default["TasksPath"]);
            if (!Directory.Exists(TasksPath))
                Directory.CreateDirectory(TasksPath);
            if (TasksPath[TasksPath.Length - 1] != '\\' || TasksPath[TasksPath.Length - 1] != '/')
                TasksPath += "\\";
            //UpdateTasks(TasksPath);


            //TasksPath + "\\" + TaskID + "\\Task.json"
            //Tasks = FF.GetAllTasksByIds(FF.GetAllIds()).Reverse().ToDictionary(pv => pv.id, pv => pv);
            Dispatcher.Invoke(() => {

                eprbrowser.NavigateToString("<html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=10\"><style>table {width: 100%; border: 1px solid #333; border-collapse: collapse !important;}td {border-right: 1px solid #333; padding: 0.375rem;} tr:not(:last-child) {border-bottom: 1px solid #ccc;}</style></head><body>" + FF.EPR() + "</body></html>");
                EmailLabel.Content = FF.Email;
                IDLabel.Content = FF.Username;
                UserImage.Source = FF.GetUserImage();
                TaskStack.ItemsSource = Tasks.Values;
            });

            List<Firefly.FFEvent> TodayEvents = new List<Firefly.FFEvent>();
            //List<TimetablePeriod> periods = new List<TimetablePeriod>();
            Firefly.FFEvent[] Events = FF.GetEvents(DateTime.Now.AddDays(-5), DateTime.Now.AddDays(10));
            foreach (Firefly.FFEvent item in Events)
            {
                DateTime startlocal = item.start.ToLocalTime();
                DateTime endlocal = item.end.ToLocalTime();
                //Item is in this month 

                PlannerStack.Dispatcher.Invoke(new Action(() =>
                {
                    Label lbl = new Label()
                    {
                        Content = "Start " + item.start.ToLocalTime() + " End " + item.end.ToLocalTime() + " Guid: " + item.guid + " Subject: " + item.subject + " Desc: " + item.description + " Loc: " + item.location
                    };
                    PlannerStack.Items.Add(lbl);
                }));
            }
            TimetablePeriod[] todayPeriods = Timetablehandler.OverlapCheck(Timetablehandler.FillInTable(Timetablehandler.ParseEventsToPeriods(Timetablehandler.FilterTodayOnly(Events)), DateTime.Now, false));
            foreach (TimetablePeriod item in todayPeriods)
            {
                Console.WriteLine("Period " + item.period + " Class " + item.Classcode + " Room: " + item.Roomcode + " start: " +item.Start.ToLocalTime() + " End: " +item.End.ToLocalTime());
            }
            ClockWindow.SetSchedule(todayPeriods.ToList());


            //Environment.ExpandEnvironmentVariables((string)Properties.Settings.Default["TasksPath"])
        }

        private int[] UpdateTasks(string filepath)
        {
            int[] AllIDs = FF.GetAllIds();
            Array.Reverse(AllIDs); //Reverse Order of the IDs 
            List<int> NewIDs = new List<int>();
            foreach (int ID in AllIDs)
            {
                if (Tasks.ContainsKey(ID))
                {
                    //Update the cached version
                    Firefly.Response[] tmpresps= FF.GetResponseForID(ID);
                    if (tmpresps == null)
                        continue;
                    Tasks[ID] = FF.UpdateResponses(Tasks[ID], tmpresps);
                    SaveTask(filepath + ID + @"\Task.json" ,Tasks[ID]);
                }
                else
                {
                    NewIDs.Add(ID);
                }
            }

            Firefly.FullTask[] tmp = FF.GetAllTasksByIds(NewIDs.ToArray());
            foreach (Firefly.FullTask item in tmp)
            {
                Tasks.Add(item.id, item);
                SaveTask(filepath + item.id + @"\Task.json", item);
            }

            //return newly added items
            return NewIDs.ToArray();
        }

        private void SaveTask(string FilePath, Firefly.FullTask task)
        {
            try
            {
                Directory.CreateDirectory(FilePath.Substring(0,FilePath.LastIndexOf("\\"))); //Creates the required directories 
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(task, Formatting.Indented));
            }
            catch(Exception e)
            {
                logger.Warn("Unable to save task - " + task.id);
            }
        }

        private void TaskStack_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            MainTabControl.SelectedIndex = 5;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ClockWindow?.Close();
        }
        
    }
}
