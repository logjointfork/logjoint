﻿using System;

namespace LogJoint.UI
{
	public static class ComponentsInitializer
	{
		public static void WireupDependenciesAndInitMainWindow(MainWindowAdapter mainWindow)
		{
			var tracer = new LJTraceSource("app", "app");
			tracer.Info("starting app");


			using (tracer.NewFrame)
			{
				ILogProviderFactoryRegistry logProviderFactoryRegistry = new LogProviderFactoryRegistry();
				IFormatDefinitionsRepository formatDefinitionsRepository = new DirectoryFormatsRepository(null);
				TempFilesManager tempFilesManager = LogJoint.TempFilesManager.GetInstance();
				IUserDefinedFormatsManager userDefinedFormatsManager = new UserDefinedFormatsManager(
					formatDefinitionsRepository, logProviderFactoryRegistry, tempFilesManager);
				new AppInitializer(tracer, userDefinedFormatsManager, logProviderFactoryRegistry);
				tracer.Info("app initializer created");

				IInvokeSynchronization invokingSynchronization = new InvokeSynchronization(new NSSynchronizeInvoke());

				UI.HeartBeatTimer heartBeatTimer = new UI.HeartBeatTimer();
				UI.Presenters.IViewUpdates viewUpdates = heartBeatTimer;

				IFiltersFactory filtersFactory = new FiltersFactory();
				IBookmarksFactory bookmarksFactory = new BookmarksFactory();
				var bookmarks = bookmarksFactory.CreateBookmarks();
				var persistentUserDataFileSystem = Persistence.Implementation.DesktopFileSystemAccess.CreatePersistentUserDataFileSystem();

				IShutdown shutdown = new Shutdown();

				Persistence.Implementation.IStorageManagerImplementation userDataStorage = new Persistence.Implementation.StorageManagerImplementation();
				Persistence.IStorageManager storageManager = new Persistence.PersistentUserDataManager(
					userDataStorage,
					shutdown
				);
				Settings.IGlobalSettingsAccessor globalSettingsAccessor = new Settings.GlobalSettingsAccessor(storageManager);
				userDataStorage.Init(
					new Persistence.Implementation.RealTimingAndThreading(),
					persistentUserDataFileSystem,
					new Persistence.PersistentUserDataManager.ConfigAccess(globalSettingsAccessor)
				);
				Persistence.IFirstStartDetector firstStartDetector = persistentUserDataFileSystem;
				Persistence.Implementation.IStorageManagerImplementation contentCacheStorage = new Persistence.Implementation.StorageManagerImplementation();
				contentCacheStorage.Init(
					new Persistence.Implementation.RealTimingAndThreading(),
					Persistence.Implementation.DesktopFileSystemAccess.CreateCacheFileSystemAccess(),
					new Persistence.ContentCacheManager.ConfigAccess(globalSettingsAccessor)
				);
				LogJoint.Properties.WebContentConfig webContentConfig = new Properties.WebContentConfig();
				Persistence.IContentCache contentCache = new Persistence.ContentCacheManager(contentCacheStorage);
				Persistence.IWebContentCache webContentCache = new Persistence.WebContentCache(
					contentCache,
					webContentConfig
				);
				MultiInstance.IInstancesCounter instancesCounter = new MultiInstance.InstancesCounter(shutdown);
				Progress.IProgressAggregatorFactory progressAggregatorsFactory = new Progress.ProgressAggregator.Factory(heartBeatTimer, invokingSynchronization);
				Progress.IProgressAggregator progressAggregator = progressAggregatorsFactory.CreateProgressAggregator();

				IAdjustingColorsGenerator colorGenerator = new AdjustingColorsGenerator(
					new PastelColorsGenerator(),
					globalSettingsAccessor.Appearance.ColoringBrightness
				);

				IModelThreads modelThreads = new ModelThreads(colorGenerator);

				ILogSourcesManager logSourcesManager = new LogSourcesManager(
					heartBeatTimer,
					invokingSynchronization,
					modelThreads,
					tempFilesManager,
					storageManager,
					bookmarks,
					globalSettingsAccessor
				);

				Telemetry.ITelemetryCollector telemetryCollector = new Telemetry.TelemetryCollector(
					storageManager,
					new Telemetry.ConfiguredAzureTelemetryUploader(),
					invokingSynchronization,
					instancesCounter,
					shutdown,
					logSourcesManager
				);
				tracer.Info("telemetry created");

				new Telemetry.UnhandledExceptionsReporter(telemetryCollector);

				MRU.IRecentlyUsedEntities recentlyUsedLogs = new MRU.RecentlyUsedEntities(
					storageManager,
					logProviderFactoryRegistry,
					telemetryCollector
				);
				IFormatAutodetect formatAutodetect = new FormatAutodetect(recentlyUsedLogs, logProviderFactoryRegistry, tempFilesManager);

				Workspaces.IWorkspacesManager workspacesManager = new Workspaces.WorkspacesManager(
					logSourcesManager,
					logProviderFactoryRegistry,
					storageManager,
					new Workspaces.Backend.AzureWorkspacesBackend(),
					tempFilesManager,
					recentlyUsedLogs,
					shutdown
				);

				AppLaunch.ILaunchUrlParser launchUrlParser = new AppLaunch.LaunchUrlParser();

				Preprocessing.IPreprocessingManagerExtensionsRegistry preprocessingManagerExtensionsRegistry = 
					new Preprocessing.PreprocessingManagerExtentionsRegistry();

				Preprocessing.ICredentialsCache preprocessingCredentialsCache = new PreprocessingCredentialsCache(
					mainWindow.Window,
					storageManager.GlobalSettingsEntry,
					invokingSynchronization
				);

				WebBrowserDownloader.IDownloader webBrowserDownloader = new UI.Presenters.WebBrowserDownloader.Presenter(
					new LogJoint.UI.WebBrowserDownloaderWindowController(),
					invokingSynchronization,
					webContentCache,
					shutdown
				);

				Preprocessing.IPreprocessingStepsFactory preprocessingStepsFactory = new Preprocessing.PreprocessingStepsFactory(
					workspacesManager,
					launchUrlParser,
					invokingSynchronization,
					preprocessingManagerExtensionsRegistry,
					progressAggregator,
					webContentCache,
					preprocessingCredentialsCache,
					logProviderFactoryRegistry,
					webBrowserDownloader,
					webContentConfig
				);

				Preprocessing.ILogSourcesPreprocessingManager logSourcesPreprocessings = new Preprocessing.LogSourcesPreprocessingManager(
					invokingSynchronization,
					formatAutodetect,
					preprocessingManagerExtensionsRegistry,
					new Preprocessing.BuiltinStepsExtension(preprocessingStepsFactory),
					telemetryCollector,
					tempFilesManager
				);

				ILogSourcesController logSourcesController = new LogSourcesController(
					logSourcesManager,
					logSourcesPreprocessings,
					recentlyUsedLogs,
					shutdown
				);

				ISearchManager searchManager = new SearchManager(
					logSourcesManager,
					progressAggregatorsFactory,
					invokingSynchronization,
					globalSettingsAccessor,
					telemetryCollector,
					heartBeatTimer
				);

				ISearchHistory searchHistory = new SearchHistory(
					storageManager.GlobalSettingsEntry
				);

				IBookmarksController bookmarksController = new BookmarkController(
					bookmarks,
					modelThreads,
					heartBeatTimer
				);

				IFiltersManager filtersManager = new FiltersManager(
					filtersFactory,
					globalSettingsAccessor,
					logSourcesManager,
					colorGenerator,
					shutdown
				);

				tracer.Info("model creation finished");

				AutoUpdate.IAutoUpdater autoUpdater = new AutoUpdate.AutoUpdater(
					instancesCounter,
					new AutoUpdate.ConfiguredAzureUpdateDownloader(),
					tempFilesManager,
					shutdown,
					invokingSynchronization,
					firstStartDetector
				);
	
				var presentersFacade = new UI.Presenters.Facade();
				UI.Presenters.IPresentersFacade navHandler = presentersFacade;

				UI.Presenters.IClipboardAccess clipboardAccess = new UI.ClipboardAccess();
				UI.Presenters.IAlertPopup alerts = new UI.AlertPopup();
				UI.Presenters.IShellOpen shellOpen = new UI.ShellOpen();

				UI.Presenters.LogViewer.IPresenterFactory logViewerPresenterFactory = new UI.Presenters.LogViewer.PresenterFactory(
					heartBeatTimer,
					presentersFacade,
					clipboardAccess,
					bookmarksFactory,
					telemetryCollector,
					logSourcesManager,
					invokingSynchronization,
					modelThreads,
					filtersManager.HighlightFilters,
					bookmarks,
					globalSettingsAccessor,
					searchManager,
					filtersFactory
				);

				UI.Presenters.LoadedMessages.IView loadedMessagesView = mainWindow.LoadedMessagesControlAdapter;
				UI.Presenters.LoadedMessages.IPresenter loadedMessagesPresenter = new UI.Presenters.LoadedMessages.Presenter(
					logSourcesManager,
					bookmarks,
					loadedMessagesView,
					heartBeatTimer,
					logViewerPresenterFactory);

				UI.Presenters.LogViewer.IPresenter viewerPresenter = loadedMessagesPresenter.LogViewerPresenter;

				UI.Presenters.StatusReports.IPresenter statusReportPresenter = new UI.Presenters.StatusReports.Presenter(
					mainWindow.StatusPopupControlAdapter,
					heartBeatTimer
				);

				UI.Presenters.SourcePropertiesWindow.IPresenter sourcePropertiesWindowPresenter = new UI.Presenters.SourcePropertiesWindow.Presenter(
					new UI.SourcePropertiesDialogView(),
					logSourcesManager,
					logSourcesPreprocessings,
					navHandler,
					alerts,
					clipboardAccess,
					shellOpen
				);

				UI.Presenters.SourcesList.IPresenter sourcesListPresenter = new UI.Presenters.SourcesList.Presenter(
					logSourcesManager,
					mainWindow.SourcesManagementControlAdapter.SourcesListControlAdapter,
					logSourcesPreprocessings,
					sourcePropertiesWindowPresenter,
					viewerPresenter,
					navHandler,
					alerts,
					clipboardAccess,
					shellOpen
				);

				UI.Presenters.SearchResult.IPresenter searchResultPresenter = new UI.Presenters.SearchResult.Presenter(
					searchManager,
					bookmarks,
					filtersManager.HighlightFilters,
					mainWindow.SearchResultsControlAdapter,
					navHandler,
					loadedMessagesPresenter,
					heartBeatTimer,
					invokingSynchronization,
					statusReportPresenter,
					logViewerPresenterFactory
				);

				UI.Presenters.SearchPanel.IPresenter searchPanelPresenter = new UI.Presenters.SearchPanel.Presenter(
					mainWindow.SearchPanelControlAdapter,
					searchManager,
					searchHistory,
					logSourcesManager,
					mainWindow,
					loadedMessagesPresenter,
					searchResultPresenter,
					statusReportPresenter
				);
				tracer.Info("search panel presenter created");

				UI.Presenters.HistoryDialog.IView historyDialogView = new UI.HistoryDialogAdapter();
				UI.Presenters.HistoryDialog.IPresenter historyDialogPresenter = new UI.Presenters.HistoryDialog.Presenter(
					logSourcesController,
					historyDialogView,
					logSourcesPreprocessings,
					preprocessingStepsFactory,
					recentlyUsedLogs,
					new UI.Presenters.QuickSearchTextBox.Presenter(historyDialogView.QuickSearchTextBox),
					alerts
				);

				AppLaunch.ICommandLineHandler commandLineHandler = new AppLaunch.CommandLineHandler(
					logSourcesPreprocessings,
					preprocessingStepsFactory
				);

				UI.Presenters.NewLogSourceDialog.IPagePresentersRegistry newLogSourceDialogPagesPresentersRegistry = 
					new UI.Presenters.NewLogSourceDialog.PagePresentersRegistry();

				UI.Presenters.NewLogSourceDialog.IPresenter newLogSourceDialogPresenter = new UI.Presenters.NewLogSourceDialog.Presenter(
					logProviderFactoryRegistry,
					newLogSourceDialogPagesPresentersRegistry,
					recentlyUsedLogs,
					new UI.NewLogSourceDialogView(),
					userDefinedFormatsManager,
					() => new UI.Presenters.NewLogSourceDialog.Pages.FormatDetection.Presenter(
						new LogJoint.UI.FormatDetectionPageController(),
						logSourcesPreprocessings,
						preprocessingStepsFactory
					),
					null // formatsWizardPresenter
				);

				newLogSourceDialogPagesPresentersRegistry.RegisterPagePresenterFactory(
					StdProviderFactoryUIs.FileBasedProviderUIKey,
					f => new UI.Presenters.NewLogSourceDialog.Pages.FileBasedFormat.Presenter(
						new LogJoint.UI.FileBasedFormatPageController(), 
						(IFileBasedLogProviderFactory)f,
						logSourcesController,
						alerts
					)
				);

				UI.Presenters.SharingDialog.IPresenter sharingDialogPresenter = new UI.Presenters.SharingDialog.Presenter(
					logSourcesManager,
					workspacesManager,
					logSourcesPreprocessings,
					alerts,
					clipboardAccess,
					new UI.SharingDialogController()
				);

				UI.Presenters.SourcesManager.IPresenter sourcesManagerPresenter = new UI.Presenters.SourcesManager.Presenter(
					logSourcesManager,
					userDefinedFormatsManager,
					recentlyUsedLogs,
					logSourcesPreprocessings,
					logSourcesController,
					mainWindow.SourcesManagementControlAdapter,
					preprocessingStepsFactory,
					workspacesManager,
					sourcesListPresenter,
					newLogSourceDialogPresenter,
					heartBeatTimer,
					sharingDialogPresenter,
					historyDialogPresenter,
					presentersFacade,
					sourcePropertiesWindowPresenter,
					alerts
				);

				UI.Presenters.BookmarksList.IPresenter bookmarksListPresenter = new UI.Presenters.BookmarksList.Presenter(
					bookmarks, 
					logSourcesManager,
					mainWindow.BookmarksManagementControlAdapter.ListView,
					heartBeatTimer,
					loadedMessagesPresenter,
					clipboardAccess
				);

				UI.Presenters.BookmarksManager.IPresenter bookmarksManagerPresenter = new UI.Presenters.BookmarksManager.Presenter(
					bookmarks,
					mainWindow.BookmarksManagementControlAdapter,
					viewerPresenter,
					searchResultPresenter,
					bookmarksListPresenter,
					statusReportPresenter,
					navHandler,
					viewUpdates,
					alerts
				);

				UI.Presenters.MainForm.IDragDropHandler dragDropHandler = new UI.DragDropHandler(
					logSourcesPreprocessings,
					preprocessingStepsFactory,
					logSourcesController
				);

				new UI.LogsPreprocessorUI(
					logSourcesPreprocessings,
					statusReportPresenter
				);

				UI.Presenters.About.IPresenter aboutDialogPresenter = new UI.Presenters.About.Presenter(
					new UI.AboutDialogAdapter(),
					new UI.AboutDialogConfig(),
					clipboardAccess,
					autoUpdater
				);

				UI.Presenters.Timeline.IPresenter timelinePresenter = new UI.Presenters.Timeline.Presenter(
					logSourcesManager,
					logSourcesPreprocessings,
					searchManager,
					bookmarks,
					mainWindow.TimelinePanelControlAdapter.TimelineControlAdapter,
					viewerPresenter,
					statusReportPresenter,
					null, // tabUsageTracker
					heartBeatTimer
				);

				new UI.Presenters.TimelinePanel.Presenter(
					logSourcesManager,
					bookmarks,
					mainWindow.TimelinePanelControlAdapter,
					timelinePresenter,
					heartBeatTimer
				);

				UI.Presenters.TimestampAnomalyNotification.IPresenter timestampAnomalyNotification = new UI.Presenters.TimestampAnomalyNotification.Presenter(
					logSourcesManager,
					logSourcesPreprocessings,
					invokingSynchronization,
					heartBeatTimer,
					presentersFacade,
					statusReportPresenter
				);
				timestampAnomalyNotification.GetHashCode(); // to suppress warning

				UI.Presenters.MainForm.IPresenter mainFormPresenter = new UI.Presenters.MainForm.Presenter(
					logSourcesManager,
					logSourcesPreprocessings,
					mainWindow,
					viewerPresenter,
					searchResultPresenter,
					searchPanelPresenter,
					sourcesListPresenter,
					sourcesManagerPresenter,
					null,//messagePropertiesDialogPresenter,
					loadedMessagesPresenter,
					bookmarksManagerPresenter,
					heartBeatTimer,
					null,//tabUsageTracker,
					statusReportPresenter,
					dragDropHandler,
					navHandler,
					autoUpdater,
					progressAggregator,
					alerts,
					sharingDialogPresenter,
					shutdown
				);
				tracer.Info("main form presenter created");

				CustomURLSchemaEventsHandler.Instance.Init(
					mainFormPresenter,
					commandLineHandler,
					invokingSynchronization
				);

				presentersFacade.Init(
					null, //messagePropertiesDialogPresenter,
					null, //threadsListPresenter,
					sourcesListPresenter,
					bookmarksManagerPresenter,
					mainFormPresenter,
					aboutDialogPresenter,
					null, //optionsDialogPresenter,
					historyDialogPresenter
				);

				var extensibilityEntryPoint = new Extensibility.Application(
					new Extensibility.Model(
						invokingSynchronization,
						telemetryCollector,
						webContentCache,
						contentCache,
						storageManager,
						bookmarks,
						logSourcesManager,
						modelThreads,
						tempFilesManager,
						preprocessingManagerExtensionsRegistry,
						logSourcesPreprocessings,
						preprocessingStepsFactory,
						progressAggregator,
						logProviderFactoryRegistry,
						userDefinedFormatsManager,
						recentlyUsedLogs,
						progressAggregatorsFactory,
						heartBeatTimer,
						logSourcesController,
						shutdown,
						webBrowserDownloader
					),
					new Extensibility.Presentation(
						loadedMessagesPresenter,
						clipboardAccess,
						presentersFacade,
						sourcesManagerPresenter,
						newLogSourceDialogPresenter,
						shellOpen,
						alerts
					),
					new Extensibility.View(
					)
				);

				new Extensibility.PluginsManager(
					extensibilityEntryPoint,
					mainFormPresenter,
					telemetryCollector,
					shutdown
				);
			}
		}
	}
}

