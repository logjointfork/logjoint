using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Diagnostics;
using System.Linq;

using MessageFlag = LogJoint.MessageBase.MessageFlag;

namespace LogJoint.UI
{
	public partial class MainForm : 
		Form,
		IModelHost,
		IUINavigationHandler,
		Presenters.ThreadsList.Presenter.ICallback,
		IMessagePropertiesFormHost,
		Presenters.SearchResult.Presenter.ICallback,
		Presenters.LogViewer.Presenter.ICallback
	{
		LJTraceSource tracer = LJTraceSource.EmptyTracer;
		readonly InvokeSynchronization invokingSynchronization;
		Model model;
		NewLogSourceDialog newLogSourceDialog;
		int liveUpdateLock;
		int updateTimeTick = 0;
		int liveLogsTick = 0;
		DateTime? lastTimeTabHasBeenUsed;
		TempFilesManager tempFilesManager;
		Control focusedControlBeforeWaitState;
		bool isAnalizing;
		LogJointApplication pluginEntryPoint;
		PluginsManager pluginsManager;
		Action longRunningProcessCancellationRoutine;
		readonly KeyValuePair<CheckBox, MessageFlag>[] checkListBoxAndFlags;
		readonly DragDropHandler dragDropHandler;
		readonly LogsPreprocessorUI logsPreprocessorUI;
		readonly StatusPopupsManager statusPopups;
		
		Presenters.ThreadsList.Presenter threadsListPresenter;
		Presenters.LogViewer.Presenter viewerPresenter;
		Presenters.LoadedMessages.IPresenter loadedMessagesPresenter;
		Presenters.SearchResult.Presenter searchResultPresenter;
		Presenters.SourcesList.IPresenter sourcesListPresenter;
		Presenters.SourcesManager.IPresenter sourcesManagerPresenter;
		Presenters.BookmarksList.IPresenter bookmarksListPresenter;
		Presenters.FiltersListBox.IPresenter filtersListPresenter;
		Presenters.FiltersManager.IPresenter displayFiltersManagerPresenter;
		Presenters.FiltersManager.IPresenter hlFiltersManagerPresenter;
		MessagePropertiesForm propertiesForm;

		public MainForm()
		{
			Thread.CurrentThread.Name = "Main thread";

			try
			{
				tracer = new LJTraceSource("TraceSourceApp");
			}
			catch (Exception)
			{
				Debug.Write("Failed to create tracer");
			}

			using (tracer.NewFrame)
			{
				invokingSynchronization = new InvokeSynchronization(this);

				tempFilesManager = LogJoint.TempFilesManager.GetInstance();

				InitializeComponent();
				AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
				{
					string msg = "Unhahdled domain exception occured";
					if (e.ExceptionObject is Exception)
						tracer.Error((Exception)e.ExceptionObject, msg);
					else
						tracer.Error("{0}: ({1}) {2}", msg, e.ExceptionObject.GetType(), e.ExceptionObject);
				};

				this.checkListBoxAndFlags = new KeyValuePair<CheckBox, MessageFlag>[]
				{ 
					new KeyValuePair<CheckBox, MessageFlag>(searchMessageTypeCheckBox0, MessageFlag.Content | MessageFlag.Error),
					new KeyValuePair<CheckBox, MessageFlag>(searchMessageTypeCheckBox1, MessageFlag.Content | MessageFlag.Warning),
					new KeyValuePair<CheckBox, MessageFlag>(searchMessageTypeCheckBox2, MessageFlag.Content | MessageFlag.Info),
					new KeyValuePair<CheckBox, MessageFlag>(searchMessageTypeCheckBox3, MessageFlag.StartFrame | MessageFlag.EndFrame)
				};

				model = new Model(this);

				timeLineControl.SetHost(model);
				timeLineControl.BeginTimeRangeDrag += delegate(object sender, EventArgs args)
				{
					liveUpdateLock++;
				};
				timeLineControl.EndTimeRangeDrag += delegate(object sender, EventArgs args)
				{
					liveUpdateLock--;
				};

				Presenters.LoadedMessages.IView loadedMessagesView = loadedMessagesControl;
				loadedMessagesPresenter = new Presenters.LoadedMessages.Presenter(model, loadedMessagesView, this);
				loadedMessagesView.SetPresenter(loadedMessagesPresenter);

				viewerPresenter = loadedMessagesPresenter.LogViewerPresenter;

				viewerPresenter.ManualRefresh += delegate(object sender, EventArgs args)
				{
					using (tracer.NewFrame)
					{
						tracer.Info("----> User Command: Refresh");
						model.Refresh();
					}
				};
				viewerPresenter.BeginShifting += delegate(object sender, EventArgs args)
				{
					SetWaitState(true);
					CreateNewStatusReport().ShowStatusText("Moving in-memory window...", false);
					cancelLongRunningProcessLabel.Visible = true;
					cancelLongRunningProcessDropDownButton.Visible = true;
					longRunningProcessCancellationRoutine = model.SourcesManager.CancelShifting;
				};
				viewerPresenter.EndShifting += delegate(object sender, EventArgs args)
				{
					longRunningProcessCancellationRoutine = null;
					cancelLongRunningProcessLabel.Visible = false;
					cancelLongRunningProcessDropDownButton.Visible = false;
					CreateNewStatusReport().Dispose();
					SetWaitState(false);
				};
				viewerPresenter.FocusedMessageChanged += delegate(object sender, EventArgs args)
				{
					timeLineControl.Invalidate();
					model.OnCurrentViewPositionChanged(viewerPresenter.FocusedMessageTime);
					searchResultPresenter.MasterFocusedMessage = viewerPresenter.FocusedMessage;
					bookmarksListPresenter.SetMasterFocusedMessage(viewerPresenter.FocusedMessage);
					pluginEntryPoint.FireFocusedMessageChanged();
					if (FocusedMessageChanged != null)
						FocusedMessageChanged(sender, args);
					if (GetPropertiesForm() != null)
						GetPropertiesForm().UpdateView(FocusedMessage);
				};
				viewerPresenter.DefaultFocusedMessageActionCaption = "Show properties...";
				viewerPresenter.DefaultFocusedMessageAction += (s, e) =>
				{
					UINavigationHandler.ShowMessageProperties();
				};

				searchResultPresenter = new Presenters.SearchResult.Presenter(model, searchResultView, this);
				searchResultView.SetPresenter(searchResultPresenter);
				searchResultPresenter.OnClose += (sender, args) => ShowSearchResultPanel(false);
				searchResultPresenter.OnResizingStarted += (sender, args) => splitContainer3.BeginSplitting();

				threadsListPresenter = new UI.Presenters.ThreadsList.Presenter(model, threadsListView, this);
				threadsListView.SetPresenter(threadsListPresenter);


				var sourcesListPresenterImpl = new Presenters.SourcesList.Presenter(
					model,
					sourcesListView.SourcesListView, 
					new Presenters.SourcePropertiesWindow.Presenter(new SourceDetailsWindowView(), this),
					this.viewerPresenter,
					this);
				sourcesListPresenter = sourcesListPresenterImpl;
				sourcesListView.SourcesListView.SetPresenter(sourcesListPresenterImpl);

				var sourcesManagerPresenterImpl = new Presenters.SourcesManager.Presenter(
					model,
					sourcesListView,
					sourcesListPresenter,
					new Presenters.NewLogSourceDialog.Presenter(model, new NewLogSourceDialogView(), logsPreprocessorUI),
					logsPreprocessorUI
				);
				sourcesManagerPresenter = sourcesManagerPresenterImpl;
				sourcesListView.SetPresenter(sourcesManagerPresenterImpl);
				sourcesManagerPresenter.OnBusyState += (_, evt) => SetWaitState(evt.BusyStateRequired);

				timeLineControl.RangeChanged += delegate(object sender, EventArgs args)
				{
					UpdateMillisecondsAvailability();
					model.Updates.InvalidateTimeGapsRange();
				};

				Func<FiltersList, FiltersManagerView, Presenters.FiltersManager.IPresenter> createFiltersManager = (filters, view) =>
				{
					var dialogPresenter = new Presenters.FilterDialog.Presenter(model, filters, new UI.FilterDialogView());
					var filtersListPresenterImpl = new Presenters.FiltersListBox.Presenter(model, filters, view.FiltersListView, dialogPresenter);
					var managerPresenterImpl = new Presenters.FiltersManager.Presenter(
						model, filters, view, filtersListPresenterImpl, dialogPresenter, viewerPresenter);
					view.SetPresenter(managerPresenterImpl);
					view.FiltersListView.SetPresenter(filtersListPresenterImpl);
					Presenters.FiltersManager.IPresenter ret = managerPresenterImpl;
					ret.FilteringResultJustAffected += (s, e) => UpdateView(false);
					return managerPresenterImpl;
				};

				displayFiltersManagerPresenter = createFiltersManager(model.DisplayFilters, displayFiltersManagementView);
				filtersListPresenter = displayFiltersManagerPresenter.FiltersListPresenter;

				hlFiltersManagerPresenter = createFiltersManager(model.HighlightFilters, hlFiltersManagementView);

				searchTextBox.Search += delegate(object sender, EventArgs args)
				{
					DoSearch(false);
				};
				searchTextBox.Escape += delegate(object sender, EventArgs args)
				{
					if (loadedMessagesControl.CanFocus)
						loadedMessagesControl.Focus();
				};

				var bookmarksListPresenterImpl = new Presenters.BookmarksList.Presenter(model, bookmarksView);
				bookmarksListPresenter = bookmarksListPresenterImpl;
				bookmarksView.SetPresenter(bookmarksListPresenterImpl);
				bookmarksListPresenter.Click += (s, bmk) =>
					NavigateToBookmark(bmk, null, BookmarkNavigationOptions.EnablePopups | BookmarkNavigationOptions.BookmarksStringsSet);

				timelineControlPanel.SetHost(model);
				timelineControlPanel.Zoom += delegate(object sender, TimelineControlEventArgs args)
				{
					timeLineControl.Zoom(args.Delta);
				};
				timelineControlPanel.Scroll += delegate(object sender, TimelineControlEventArgs args)
				{
					timeLineControl.Scroll(args.Delta);
				};
				timelineControlPanel.ZoomToViewAll += delegate(object sender, EventArgs args)
				{
					timeLineControl.ZoomToViewAll();
				};
				timelineControlPanel.ViewTailMode += delegate(object sender, ViewTailModeRequestEventArgs args)
				{
					if (args.ViewTailModeRequested)
						timeLineControl.TrySwitchOnViewTailMode();
					else
						timeLineControl.TrySwitchOffViewTailMode();
				};

				model.SourcesManager.OnSearchStarted += (sender, args) =>
				{
					SetWaitState(true);
					CreateNewStatusReport().ShowStatusText("Searching...", false);
					cancelLongRunningProcessLabel.Visible = true;
					cancelLongRunningProcessDropDownButton.Visible = true;
					longRunningProcessCancellationRoutine = model.SourcesManager.CancelSearch;
				};
				model.SourcesManager.OnSearchCompleted += (sender, args) =>
				{
					longRunningProcessCancellationRoutine = null;
					cancelLongRunningProcessLabel.Visible = false;
					cancelLongRunningProcessDropDownButton.Visible = false;
					CreateNewStatusReport().Dispose();
					SetWaitState(false);
				};

				UpdateSearchHistoryList();
				model.SearchHistory.OnChanged += (sender, args) => UpdateSearchHistoryList();

				statusPopups = new StatusPopupsManager(this, toolStripStatusLabel);

				this.logsPreprocessorUI = new LogsPreprocessorUI(this, model.GlobalSettings);
				this.dragDropHandler = new DragDropHandler(model.LogSourcesPreprocessings, logsPreprocessorUI);

				UpdateSearchControls();

				InitLogFactories();
				UserDefinedFormatsManager.DefaultInstance.ReloadFactories();

				this.pluginEntryPoint = new LogJointApplication(model, this, viewerPresenter, filtersListPresenter);
				this.pluginsManager = new PluginsManager(tracer, pluginEntryPoint, menuTabControl);
			}
		}

		private void UpdateSearchHistoryList()
		{
			searchTextBox.Items.Clear();
			foreach (var entry in model.SearchHistory.Items)
				searchTextBox.Items.Add(entry);
		}

		MessagePropertiesForm GetPropertiesForm()
		{
			if (propertiesForm != null)
				if (propertiesForm.IsDisposed)
					propertiesForm = null;
			return propertiesForm;
		}

		public void ShowMessageProperties()
		{
			if (GetPropertiesForm() == null)
			{
				propertiesForm = new MessagePropertiesForm(this);
				components.Add(propertiesForm);
				AddOwnedForm(propertiesForm);
			}
			propertiesForm.UpdateView(FocusedMessage);
			propertiesForm.Show();
		}

		#region IModelHost

		public LJTraceSource Tracer { get { return tracer; } }

		public IInvokeSynchronization Invoker { get { return invokingSynchronization; } }

		public void OnIdleWhileShifting()
		{
			Application.DoEvents();
		}

		public ITempFilesManager TempFilesManager 
		{
			get { return tempFilesManager; }
		}

		public void OnNewProvider(ILogProvider provider)
		{
			model.MRU.RegisterRecentLogEntry(provider);
		}

		public void OnUpdateView()
		{
			UpdateView(false);
		}

		public IStatusReport CreateNewStatusReport()
		{
			return statusPopups.CreateNewStatusReport();
		}

		public DateTime? CurrentViewTime
		{
			get 
			{
				return viewerPresenter.FocusedMessageTime;
			}
		}

		public void SetCurrentViewTime(DateTime? time, NavigateFlag flags, ILogSource preferredSource)
		{
			using (tracer.NewFrame)
			{
				tracer.Info("time={0}, flags={1}", time, flags);
				NavigateFlag origin = flags & NavigateFlag.OriginMask;
				NavigateFlag align = flags & NavigateFlag.AlignMask;
				switch (origin)
				{
					case NavigateFlag.OriginDate:
						tracer.Info("Selecting the line at the certain time");
						viewerPresenter.SelectMessageAt(time.Value, align, preferredSource);
						break;
					case NavigateFlag.OriginStreamBoundaries:
						switch (align)
						{
							case NavigateFlag.AlignTop:
								tracer.Info("Selecting the first line");
								viewerPresenter.SelectFirstMessage();
								break;
							case NavigateFlag.AlignBottom:
								tracer.Info("Selecting the last line");
								viewerPresenter.SelectLastMessage();
								break;
						}
						break;
				}
			}
		}

		public MessageBase FocusedMessage
		{
			get
			{
				return viewerPresenter.FocusedMessage;
			}
		}

		public bool FocusRectIsRequired
		{
			get 
			{
				if (!lastTimeTabHasBeenUsed.HasValue)
					return false;
				if (DateTime.Now - lastTimeTabHasBeenUsed > TimeSpan.FromSeconds(10))
				{
					lastTimeTabHasBeenUsed = null;
					return false;
				}
				return true;
			}
		}

		public IUINavigationHandler UINavigationHandler
		{
			get { return this; }
		}

		#endregion

		#region Presenters.SearchResult.Presenter.ICallback members

		public void NavigateToFoundMessage(Bookmark foundMessageBookmark, SearchAllOccurencesParams searchParams)
		{
			if (NavigateToBookmark(foundMessageBookmark, null, BookmarkNavigationOptions.EnablePopups | BookmarkNavigationOptions.SearchResultStringsSet))
			{
				var opts = new Presenters.LogViewer.SearchOptions()
				{
					CoreOptions = searchParams.Options,
					SearchOnlyWithinFirstMessage = true,
					HighlightResult = true
				};
				opts.CoreOptions.SearchInRawText = viewerPresenter.ShowRawMessages;
				viewerPresenter.Search(opts);
				loadedMessagesControl.Focus();
			}
		}
		
		#endregion

		#region IMessagePropertiesFormHost Members

		public bool BookmarksSupported
		{
			get { return model.Bookmarks != null; }
		}

		public bool NavigationOverHighlightedIsEnabled
		{
			get
			{
				return model.HighlightFilters.FilteringEnabled &&
					model.HighlightFilters.Count > 0;
			}
		}

		public void ToggleBookmark(MessageBase line)
		{
			viewerPresenter.ToggleBookmark(line);
		}

		public void FindBegin(FrameEnd end)
		{
			viewerPresenter.GoToParentFrame();
		}

		public void FindEnd(FrameBegin end)
		{
			viewerPresenter.GoToEndOfFrame();
		}

		public void Next()
		{
			viewerPresenter.Next();
		}

		public void Prev()
		{
			viewerPresenter.Prev();
		}

		public void NextHighlighted()
		{
			viewerPresenter.GoToNextHighlightedMessage();
		}

		public void PrevHighlighted()
		{
			viewerPresenter.GoToPrevHighlightedMessage();
		}

		#endregion

		public void ExecuteThreadPropertiesDialog(IThread thread)
		{
			using (UI.ThreadPropertiesForm f = new UI.ThreadPropertiesForm(thread, this))
			{
				f.ShowDialog();
			} 
		}

		public void ForceViewUpdateAfterThreadChecked()
		{
			Action action = () => UpdateView(false);
			BeginInvoke(action);
		}

		public event EventHandler FocusedMessageChanged;

		void UpdateRawViewAvailability()
		{
			bool rawViewAllowed = model.SourcesManager.Items.Any(s => !s.IsDisposed && s.Visible && s.Provider.Factory.ViewOptions.RawViewAllowed);
			loadedMessagesPresenter.RawViewAllowed = rawViewAllowed;
			searchResultPresenter.RawViewAllowed = rawViewAllowed;
		}

		void UpdateMillisecondsAvailability()
		{
			bool timeLineWantsMilliseconds = timeLineControl.AreMillisecondsVisible;
			bool atLeastOneSourceWantMillisecondsAlways = model.SourcesManager.Items.Any(s => !s.IsDisposed && s.Visible && s.Provider.Factory.ViewOptions.AlwaysShowMilliseconds);
			viewerPresenter.ShowMilliseconds = timeLineWantsMilliseconds || atLeastOneSourceWantMillisecondsAlways;
		}

		static class Win32Native
		{
			public const int SC_CLOSE = 0xF060;
			public const int MF_GRAYED = 0x1;
			public const int MF_ENABLED = 0x0;

			[DllImport("user32.dll")]
			public static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

			[DllImport("user32.dll")]
			public static extern int EnableMenuItem(IntPtr hMenu, int wIDEnableItem, int wEnable);

			[DllImport("user32.dll")]
			public static extern IntPtr GetFocus();

			[DllImport("user32.dll")]
			public static extern IntPtr GetParent(IntPtr hWnd);
		}

		void SetWaitState(bool wait)
		{
			using (tracer.NewFrame)
			{
				if (wait)
				{
					tracer.Info("Setting wait state");
					focusedControlBeforeWaitState = Control.FromHandle(Win32Native.GetFocus());

					if (focusedControlBeforeWaitState == null
					 && searchTextBox.Focused)
					{
						// ComboBox's child EDIT returned by win32 GetFocus()
						// can not be found by Control.FromHandle().
						// Handle search box separately.
						focusedControlBeforeWaitState = searchTextBox;
					}
				}
				else
				{
					tracer.Info("Exiting from wait state");
				}
				splitContainer2.Enabled = !wait;
				splitContainer2.ForeColor = wait ? Color.Gray : Color.Black;
				Win32Native.EnableMenuItem(Win32Native.GetSystemMenu(this.Handle, false), Win32Native.SC_CLOSE,
					wait ? Win32Native.MF_GRAYED : Win32Native.MF_ENABLED);
				if (!wait)
				{
					if (focusedControlBeforeWaitState != null
					 && !focusedControlBeforeWaitState.IsDisposed
					 && focusedControlBeforeWaitState.Enabled
					 && focusedControlBeforeWaitState.CanFocus)
					{
						focusedControlBeforeWaitState.Focus();
					}
					focusedControlBeforeWaitState = null;
				}
			}
		}

		void DoSearch(bool invertDirection)
		{
			Search.Options coreOptions;
			coreOptions.Template = this.searchTextBox.Text;
			coreOptions.WholeWord = this.wholeWordCheckbox.Checked;
			coreOptions.ReverseSearch = this.searchUpCheckbox.Checked;
			if (invertDirection)
				coreOptions.ReverseSearch = !coreOptions.ReverseSearch;
			coreOptions.Regexp = this.regExpCheckBox.Checked;
			coreOptions.SearchWithinThisThread = null;
			if (this.searchWithinCurrentThreadCheckbox.Checked
			 && viewerPresenter.FocusedMessage != null)
			{
				coreOptions.SearchWithinThisThread = viewerPresenter.FocusedMessage.Thread;
			}
			coreOptions.TypesToLookFor = MessageFlag.None;
			coreOptions.MatchCase = this.matchCaseCheckbox.Checked;
			foreach (var i in this.checkListBoxAndFlags)
				if (i.Key.Checked)
					coreOptions.TypesToLookFor |= i.Value;
			coreOptions.WrapAround = wrapAroundCheckBox.Checked;
			coreOptions.MessagePositionToStartSearchFrom = viewerPresenter.FocusedMessage != null ? 
				viewerPresenter.FocusedMessage.Position : 0;
			coreOptions.SearchInRawText = viewerPresenter.ShowRawMessages;

			if (searchAllOccurencesRadioButton.Checked)
			{
				FiltersList filters = null;
				if (respectFilteringRulesCheckBox.Checked)
				{
					filters = model.DisplayFilters.Clone();
					filters.FilteringEnabled = true; // ignore global "enable filtering" switch when searching all occurences
				}
				model.SourcesManager.SearchAllOccurences(new SearchAllOccurencesParams(filters, coreOptions));
				ShowSearchResultPanel(true);
			}
			else if (searchNextMessageRadioButton.Checked)
			{
				LogJoint.UI.Presenters.LogViewer.SearchOptions so;
				so.CoreOptions = coreOptions;
				so.HighlightResult = true;
				so.SearchOnlyWithinFirstMessage = false;
				LogJoint.UI.Presenters.LogViewer.SearchResult sr;
				try
				{
					if (searchInSearchResultsCheckBox.Checked)
						sr = searchResultPresenter.Search(so);
					else
						sr = viewerPresenter.Search(so);
				}
				catch (LogJoint.UI.Presenters.LogViewer.SearchTemplateException)
				{
					MessageBox.Show("Error in search template", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}
				if (!sr.Succeeded)
				{
					CreateNewStatusReport().ShowStatusPopup("Search", GetUnseccessfulSearchMessage(so), true);
				}
			}
			model.SearchHistory.Add(new SearchHistory.SearchHistoryEntry(coreOptions));
		}

		string GetUnseccessfulSearchMessage(LogJoint.UI.Presenters.LogViewer.SearchOptions so)
		{
			List<string> options = new List<string>();
			if (!string.IsNullOrEmpty(so.CoreOptions.Template))
				options.Add(so.CoreOptions.Template);
			if ((so.CoreOptions.TypesToLookFor & MessageFlag.StartFrame) != 0)
				options.Add("Frames");
			if ((so.CoreOptions.TypesToLookFor & MessageFlag.Info) != 0)
				options.Add("Infos");
			if ((so.CoreOptions.TypesToLookFor & MessageFlag.Warning) != 0)
				options.Add("Warnings");
			if ((so.CoreOptions.TypesToLookFor & MessageFlag.Error) != 0)
				options.Add("Errors");
			if (so.CoreOptions.WholeWord)
				options.Add("Whole word");
			if (so.CoreOptions.MatchCase)
				options.Add("Match case");
			if (so.CoreOptions.ReverseSearch)
				options.Add("Search up");
			StringBuilder msg = new StringBuilder();
			msg.Append("No messages found");
			if (options.Count > 0)
			{
				msg.Append(" (");
				for (int optIdx = 0; optIdx < options.Count; ++optIdx)
					msg.AppendFormat("{0}{1}", (optIdx > 0 ? ", " : ""), options[optIdx]);
				msg.Append(")");
			}
			return msg.ToString();
		}

		private void doSearchButton_Click(object sender, EventArgs e)
		{
			DoSearch(false);
		}

		private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			using (tracer.NewFrame)
			{
				SetWaitState(true);
				try
				{
					model.Dispose();
					pluginsManager.Dispose();
				}
				finally
				{
					SetWaitState(false);
				}
			}
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			string[] args = Environment.GetCommandLineArgs();

			if (args.Length > 1)
			{
				model.LogSourcesPreprocessings.Preprocess(
					args.Skip(1).Select(f => new Preprocessing.FormatDetectionStep(f)),
					logsPreprocessorUI
				);
			}
		}

		void UpdateView(bool liteUpdate)
		{
			if (!liteUpdate)
			{
				if (model.Updates.ValidateThreads())
				{
					threadsListPresenter.UpdateView();
					model.Bookmarks.PurgeBookmarksForDisposedThreads();
				}

				if (model.Updates.ValidateTimeline())
				{
					timeLineControl.UpdateView();
					timelineControlPanel.UpdateView();
				}

				if (!model.AtLeastOneSourceIsBeingLoaded())
				{
					if (model.Updates.ValidateMessages())
					{
						loadedMessagesPresenter.UpdateView();
						model.SetCurrentViewPositionIfNeeded();
					}
				}
				if (model.Updates.ValidateSearchResult())
				{
					searchResultPresenter.UpdateView();
				}

				if (model.Updates.ValidateFilters())
				{
					displayFiltersManagerPresenter.UpdateView();
				}

				if (model.Updates.ValidateHighlightFilters())
				{
					hlFiltersManagerPresenter.UpdateView();
				}

				if (model.Updates.ValidateTimeGapsRange())
				{
					foreach (var source in model.SourcesManager.Items)
						source.TimeGaps.Update(timeLineControl.TimeRange);
				}

				if (model.Updates.ValidateBookmarks())
				{
					bookmarksListPresenter.UpdateView();
				}

				SetAnalizingIndication(model.SourcesManager.Items.Any(s => s.TimeGaps.IsWorking));
			}
			if (model.Updates.ValidateSources())
			{
				sourcesManagerPresenter.UpdateView();
				UpdateRawViewAvailability();
				UpdateMillisecondsAvailability();
				pluginEntryPoint.FireSourcesChanged();
			}
		}

		private void updateViewTimer_Tick(object sender, EventArgs e)
		{
			++updateTimeTick;
			++liveLogsTick;

			if (liveUpdateLock == 0)
			{
				UpdateView((updateTimeTick % 3) != 0);
				if ((liveLogsTick % 6) == 0)
					model.PeriodicUpdate();
			}

			statusPopups.Timeslice();
		}

		void InitLogFactories()
		{
			Assembly[] asmsToAnalize = new Assembly[] { Assembly.GetEntryAssembly(), typeof(Model).Assembly };

			foreach (Assembly asm in asmsToAnalize)
			{
				foreach (Type t in asm.GetTypes())
				{
					if (t.IsClass && typeof(ILogProviderFactory).IsAssignableFrom(t))
					{
						System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
					}
				}
			}
		}

		void SetAnalizingIndication(bool analizing)
		{
			if (isAnalizing == analizing)
				return;
			toolStripAnalizingImage.Visible = analizing;
			toolStripAnalizingLabel.Visible = analizing;
			isAnalizing = analizing;
		}

		protected override bool ProcessTabKey(bool forward)
		{
			this.lastTimeTabHasBeenUsed = DateTime.Now;
			return base.ProcessTabKey(forward);
		}

		private void DoToggleBookmark()
		{
			MessageBase l = searchResultPresenter.IsViewFocused ? searchResultPresenter.FocusedMessage : viewerPresenter.FocusedMessage;
			if (l != null)
			{
				model.Bookmarks.ToggleBookmark(l);
				UpdateView(false);
			}
			else
			{
				tracer.Warning("There is no lines selected");
			}
		}

		private void toggleBookmarkButton_Click(object sender, EventArgs e)
		{
			using (tracer.NewFrame)
			{
				tracer.Info("----> User Command: Toggle Bookmark.");
				DoToggleBookmark();
			}
		}

		private void deleteAllBookmarksButton_Click(object sender, EventArgs e)
		{
			using (tracer.NewFrame)
			{
				tracer.Info("----> User Command: Clear Bookmarks.");

				if (model.Bookmarks.Count == 0)
				{
					tracer.Info("Nothing to clear");
					return;
				}

				if (MessageBox.Show(
					string.Format("You are about to delete ({0}) bookmark(s).\nAre you sure?", model.Bookmarks.Count),
					this.Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) != DialogResult.Yes)
				{
					tracer.Info("User didn't confirm the cleaning");
					return;
				}

				model.Bookmarks.Clear();
				UpdateView(false);
			}
		}

		void NextBookmark(bool forward)
		{
			var firstBmk = viewerPresenter.NextBookmark(forward);
			if (firstBmk == null)
			{
				CreateNewStatusReport().ShowStatusPopup("Bookmarks",
					forward ? "Next bookmark not found" : "Prev bookmark not found", true);
			}
			else
			{
				var firstBmkStatus = viewerPresenter.SelectMessageAt(firstBmk);
				if (firstBmkStatus != Presenters.LogViewer.Presenter.BookmarkSelectionStatus.Success)
				{
					bool reportFailure = true;
					var bookmarks = model.Bookmarks.Items;
					if (!forward)
						bookmarks = bookmarks.Reverse();
					foreach (var followingBmk in bookmarks.SkipWhile(b => b != firstBmk).Skip(1))
					{
						if (viewerPresenter.SelectMessageAt(followingBmk) == Presenters.LogViewer.Presenter.BookmarkSelectionStatus.Success)
						{
							reportFailure = false;
							break;
						}
					}
					if (reportFailure)
					{
						HandleNavigateToBookmarkFailure(firstBmkStatus, firstBmk, 
							BookmarkNavigationOptions.EnablePopups | BookmarkNavigationOptions.BookmarksStringsSet);
					}
				}
			}
		}

		private void prevBookmarkButton_Click(object sender, EventArgs e)
		{
			using (tracer.NewFrame)
			{
				tracer.Info("----> User Command: Prev Bookmark.");
				NextBookmark(false);
			}
		}

		private void nextBookmarkButton_Click(object sender, EventArgs e)
		{
			using (tracer.NewFrame)
			{
				tracer.Info("----> User Command: Next Bookmark.");
				NextBookmark(true);
			}
		}

		private void timeLineControl1_Navigate(object sender, LogJoint.UI.TimeNavigateEventArgs args)
		{
			using (tracer.NewFrame)
			{
				string preferredSourceId = args.Source != null ? args.Source.Id : null;
				ILogSource preferredSource = model.SourcesManager.Items.FirstOrDefault(
						c => c.ConnectionId != null && c.ConnectionId == preferredSourceId);
				tracer.Info("----> User Command: Navigate from timeline. Date='{0}', Flags={1}, Source={2}", args.Date, args.Flags, preferredSourceId);
				model.NavigateTo(args.Date, args.Flags, preferredSource);
			}
		}

		private void cancelLongRunningProcessDropDownButton_Click(object sender, EventArgs e)
		{
			using (tracer.NewFrame)
			{
				tracer.Info("----> User Command: Cancel long running process");
				if (longRunningProcessCancellationRoutine != null)
					longRunningProcessCancellationRoutine();
			}
		}

		private void MainForm_KeyDown(object sender, KeyEventArgs e)
		{
			if (cancelLongRunningProcessDropDownButton.Visible && e.KeyData == Keys.Escape)
				cancelLongRunningProcessDropDownButton_Click(sender, e);
			Keys keyCode = e.KeyData & Keys.KeyCode;
			if ((keyCode == Keys.F) && (e.KeyData & Keys.Control) != 0)
			{
				menuTabControl.SelectedTab = searchTabPage;
				searchTextBox.Focus();
				searchTextBox.SelectAll();
				if ((e.KeyData & Keys.Shift) != 0)
					searchAllOccurencesRadioButton.Checked = true;
			}
			else if (e.KeyData == Keys.F3)
			{
				DoSearch(false);
			}
			else if (e.KeyData == (Keys.F3 | Keys.Shift))
			{
				DoSearch(true);
			}
			else if (e.KeyData == (Keys.K | Keys.Control))
			{
				DoToggleBookmark();
			}
			else if (e.KeyData == Keys.F2)
			{
				NextBookmark(true);
			}
			else if (e.KeyData == (Keys.F2 | Keys.Shift))
			{
				NextBookmark(false);
			}
		}

		void HandleNavigateToBookmarkFailure(Presenters.LogViewer.Presenter.BookmarkSelectionStatus status, IBookmark bmk, BookmarkNavigationOptions options)
		{
			if ((options & BookmarkNavigationOptions.EnablePopups) == 0)
				return;

			string popupCaption;
			string messageDescription;
			if ((options & BookmarkNavigationOptions.BookmarksStringsSet) != 0)
			{
				popupCaption = "Bookmarks";
				messageDescription = "Bookmarked message";
			}
			else if ((options & BookmarkNavigationOptions.SearchResultStringsSet) != 0)
			{
				popupCaption = "Search result";
				messageDescription = "Message";
			}
			else
			{
				popupCaption = "Warning";
				messageDescription = "Message";
			}

			bool noLinks = (options & BookmarkNavigationOptions.NoLinksInPopups) != 0;

			if ((status & Presenters.LogViewer.Presenter.BookmarkSelectionStatus.BookmarkedMessageIsHiddenBecauseOfInvisibleThread) != 0 && bmk.Thread != null)
				CreateNewStatusReport().ShowStatusPopup(popupCaption,
					Enumerable.Repeat(new StatusMessagePart(messageDescription + " belongs to a hidden thread."), 1)
					.Union(noLinks ?
						Enumerable.Empty<StatusMessagePart>() :
						new StatusMessagePart[] {
							new StatusMessageLink("Locate", () => ShowThread(bmk.Thread)),
							new StatusMessagePart("the thread.")
						}
					), true);
			else if ((status & Presenters.LogViewer.Presenter.BookmarkSelectionStatus.BookmarkedMessageIsFilteredOut) != 0)
				CreateNewStatusReport().ShowStatusPopup(popupCaption,
					Enumerable.Repeat(new StatusMessagePart(messageDescription + " is hidden by display filters."), 1)
					.Union(noLinks ?
						Enumerable.Empty<StatusMessagePart>() :
						new StatusMessagePart[] {
							new StatusMessageLink("Change", () => ShowFiltersView()),
							new StatusMessagePart("filters.")
						}
					), true);
			else if ((status & Presenters.LogViewer.Presenter.BookmarkSelectionStatus.BookmarkedMessageNotFound) != 0)
				CreateNewStatusReport().ShowStatusPopup(popupCaption, messageDescription + " can not be shown", true);
		}

		public bool NavigateToBookmark(IBookmark bmk, Predicate<MessageBase> messageMatcherWhenNoHashIsSpecified = null, BookmarkNavigationOptions options = BookmarkNavigationOptions.Default)
		{
			var status = viewerPresenter.SelectMessageAt(bmk, messageMatcherWhenNoHashIsSpecified);
			if (status == Presenters.LogViewer.Presenter.BookmarkSelectionStatus.Success)
				return true;
			HandleNavigateToBookmarkFailure(status, bmk, options);
			return false;
		}

		#region IUINavigationHandler Members

		public void ShowLine(IBookmark bmk, BookmarkNavigationOptions options = BookmarkNavigationOptions.Default)
		{
			NavigateToBookmark(bmk, null, options);
		}

		public void ShowThread(IThread thread)
		{
			using (tracer.NewFrame)
			{
				tracer.Info("Thread={0}", thread.DisplayName);
				menuTabControl.SelectedTab = threadsTabPage;
				threadsListPresenter.Select(thread);
			}
		}

		public void ShowLogSource(ILogSource source)
		{
			using (tracer.NewFrame)
			{
				tracer.Info("Source={0}", source.DisplayName);
				menuTabControl.SelectedTab = sourcesTabPage;
				sourcesListPresenter.SelectSource(source);
			}
		}

		public void ShowFiltersView()
		{
			using (tracer.NewFrame)
			{
				menuTabControl.SelectedTab = filtersTabPage;
			}
		}

		public void SaveLogSourceAs(ILogSource logSource)
		{
			ISaveAs saveAs = logSource.Provider as ISaveAs;
			if (saveAs == null || !saveAs.IsSavableAs)
				return;
			var dlg = saveFileDialog1;
			dlg.FileName = saveAs.SuggestedFileName ?? "log.txt";
			if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
				return;
			try
			{
				saveAs.SaveAs(dlg.FileName);
			}
			catch (Exception ex)
			{
				MessageBox.Show("Failed to save file: " + ex.Message);
			}
		}

		public void SaveJointAndFilteredLog()
		{
			if (!model.ContainsEnumerableLogSources)
				return;
			var dlg = saveFileDialog1;
			dlg.FileName = "joint-log.xml";
			if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
				return;
			SetWaitState(true);
			try
			{
				using (var fs = new FileStream(dlg.FileName, FileMode.Create))
				using (var writer = new LogJoint.Writers.NativeLogWriter(fs))
					model.SaveJointAndFilteredLog(writer);
			}
			catch (Exception e)
			{
				MessageBox.Show(e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			}
			finally
			{
				SetWaitState(false);
			}
		}

		public void OpenContainingFolder(ILogSource logSource)
		{
			var intf = logSource.Provider as IOpenContainingFolder;
			if (intf == null)
				return;
			var fileToShow = intf.PathOfFileToShow;
			if (string.IsNullOrWhiteSpace(fileToShow))
				return;
			Process.Start("explorer.exe", "/select," + fileToShow);
		}

		#endregion

		private void aboutLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			if (e.Button == System.Windows.Forms.MouseButtons.Right)
			{
				DoDebugStuff();
				return;
			}
			using (AboutBox aboutBox = new AboutBox())
			{
				aboutBox.ShowDialog();
			}
		}

		IMediaBasedReaderFactory CreateColelibLogFactory()
		{
			var factory = LogProviderFactoryRegistry.DefaultInstance.Find("Skype", "Deobfuscated corelib log");
			return factory as IMediaBasedReaderFactory;
		}

		private void DoDebugStuff()
		{
		}

		private void MainForm_DragOver(object sender, DragEventArgs e)
		{
			if (dragDropHandler.ShouldAcceptDragDrop(e.Data))
				e.Effect = DragDropEffects.All;
		}

		private void MainForm_DragDrop(object sender, DragEventArgs e)
		{
			dragDropHandler.AcceptDragDrop(e.Data);
		}

		void ShowSearchResultPanel(bool show)
		{
			splitContainer3.Panel2Collapsed = !show;
			UpdateSearchControls();
		}

		private void searchModeRadioButtonChecked(object sender, EventArgs e)
		{
			UpdateSearchControls();
		}

		private void UpdateSearchControls()
		{
			respectFilteringRulesCheckBox.Enabled = searchAllOccurencesRadioButton.Checked;
			searchUpCheckbox.Enabled = searchNextMessageRadioButton.Checked;
			wrapAroundCheckBox.Enabled = searchNextMessageRadioButton.Checked;
			searchInSearchResultsCheckBox.Enabled = searchNextMessageRadioButton.Checked && !splitContainer3.Panel2Collapsed;
		}

		void Presenters.LogViewer.Presenter.ICallback.EnsureViewUpdated()
		{
			if (model.Updates.ValidateMessages())
			{
				viewerPresenter.UpdateView();
			}
		}

		private void searchTextBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (searchTextBox.SelectedIndex >= 0 && searchTextBox.SelectedIndex < searchTextBox.Items.Count)
			{
				var entry = searchTextBox.Items[searchTextBox.SelectedIndex] as SearchHistory.SearchHistoryEntry;
				if (entry != null)
				{
					regExpCheckBox.Checked = entry.Regexp;
					matchCaseCheckbox.Checked = entry.MatchCase;
					wholeWordCheckbox.Checked = entry.WholeWord;
					foreach (var i in checkListBoxAndFlags)
						i.Key.Checked = (entry.TypesToLookFor & i.Value) == i.Value;
				}
			}
		}

		private void searchTextBox_DrawItem(object sender, DrawItemEventArgs e)
		{
			e.DrawBackground();
			var entry = searchTextBox.Items[e.Index] as SearchHistory.SearchHistoryEntry;
			if (entry == null)
				return;
			using (var brush = new SolidBrush(e.ForeColor))
				e.Graphics.DrawString(entry.Description, e.Font, brush, e.Bounds);
		}

		private void rawViewToolStripButton_Click(object sender, EventArgs e)
		{
			viewerPresenter.ShowRawMessages = !viewerPresenter.ShowRawMessages;
		}
	}

}