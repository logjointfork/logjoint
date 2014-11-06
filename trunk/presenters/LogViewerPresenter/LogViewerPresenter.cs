using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Linq;
using LogJoint.RegularExpressions;
using System.Threading;

namespace LogJoint.UI.Presenters.LogViewer
{
	public class Presenter : INextBookmarkCallback
	{
		#region Public interface

		public Presenter(IModel model, IView view, IUINavigationHandler navHandler)
		{
			this.model = model;
			this.searchResultModel = model as ISearchResultModel;
			this.view = view;
			this.navHandler = navHandler;

			this.tracer = model.Tracer;

			loadedMessagesCollection = new LoadedMessagesCollection(this);
			displayMessagesCollection = new DisplayMessagesCollection(this);

			this.model.OnMessagesChanged += (sender, e) =>
			{
				displayFiltersPreprocessingResultCacheIsValid = false;
				highlightFiltersPreprocessingResultCacheIsValid = false;
			};
			this.model.DisplayFilters.OnFiltersListChanged += (sender, e) =>
			{
				displayFiltersPreprocessingResultCacheIsValid = false;
			};
			this.model.DisplayFilters.OnFilteringEnabledChanged += (sender, e) =>
			{
				displayFiltersPreprocessingResultCacheIsValid = false;
			};
			this.model.DisplayFilters.OnPropertiesChanged += (sender, e) =>
			{
				if (e.ChangeAffectsPreprocessingResult)
					displayFiltersPreprocessingResultCacheIsValid = false;
			};
			this.model.HighlightFilters.OnFiltersListChanged += (sender, e) =>
			{
				highlightFiltersPreprocessingResultCacheIsValid = false;
			};
			this.model.HighlightFilters.OnPropertiesChanged += (sender, e) =>
			{
				if (e.ChangeAffectsPreprocessingResult)
					highlightFiltersPreprocessingResultCacheIsValid = false;
			};
			this.model.HighlightFilters.OnFilteringEnabledChanged += (sender, e) =>
			{
				highlightFiltersPreprocessingResultCacheIsValid = false;
			};
			DisplayHintIfMessagesIsEmpty();

			searchResultInplaceHightlightHandler = SearchResultInplaceHightlightHandler;
		}


		public void UpdateView()
		{
			InternalUpdate();
		}

		public event EventHandler SelectionChanged;
		public event EventHandler FocusedMessageChanged;
		public event EventHandler BeginShifting;
		public event EventHandler EndShifting;
		public event EventHandler DefaultFocusedMessageAction;
		public event EventHandler ManualRefresh;
		public event EventHandler RawViewModeChanged;

		public SelectionInfo Selection { get { return selection; } }
		public LJTraceSource Tracer { get { return tracer; } }

		public DisplayMessagesCollection DisplayMessages { get { return displayMessagesCollection; } }

		public struct DisplayLine
		{
			public int DisplayLineIndex;
			public MessageBase Message;
			public int TextLineIndex;
		};

		public IEnumerable<DisplayLine> GetDisplayLines(int beginIdx, int endIdx)
		{
			int i = beginIdx;
			for (; i != endIdx; ++i)
			{
				var dm = displayMessages[i];
				yield return new DisplayLine() { DisplayLineIndex = i, Message = dm.DisplayMsg, TextLineIndex = dm.TextLineIndex };
			}
		}

		public int LoadedMessagesCount { get { return loadedMessagesCollection.Count; } }
		public int VisibleMessagesCount { get { return displayMessagesCollection.Count; } }

		public string DefaultFocusedMessageActionCaption { get { return defaultFocusedMessageActionCaption; } set { defaultFocusedMessageActionCaption = value; } }

		public Func<MessageBase, IEnumerable<Tuple<int, int>>> InplaceHighlightHandler1 
		{ 
			get 
			{
				if (searchResultModel != null && searchResultModel.SearchParams != null)
					return searchResultInplaceHightlightHandler;
				return null;
			} 
		}

		public Func<MessageBase, IEnumerable<Tuple<int, int>>> InplaceHighlightHandler2
		{
			get { return selectionInplaceHighlightingHandler; }
		}

		public bool OulineBoxClicked(MessageBase msg, bool controlIsHeld)
		{
			if (!(msg is FrameBegin))
				return false;
			DoExpandCollapse(msg, controlIsHeld, new bool?());
			return true;
		}

		public enum LogFontSize
		{
			ExtraSmall = -2,
			Small = -1,
			Normal = 0,
			Large1 = 1,
			Large2 = 2,
			Large3 = 3,
			Large4 = 4,
			Large5 = 5,
			Minimum = ExtraSmall,
			Maximum = Large5
		};

		public LogFontSize FontSize
		{
			get { return fontSize; }
			set
			{
				if (value != fontSize)
				{
					view.UpdateStarted();
					try
					{
						fontSize = value;
						view.OnFontSizeChanged();
						view.UpdateScrollSizeToMatchVisibleCount();
					}
					finally
					{
						view.UpdateFinished();
					}
					view.Invalidate();
				}
			}
		}

		public bool ShowTime
		{
			get { return showTime; }
			set
			{
				if (showTime == value)
					return;
				showTime = value;
				view.Invalidate();
			}
		}

		public bool ShowMilliseconds
		{
			get
			{
				return showMilliseconds;
			}
			set
			{
				if (showMilliseconds == value)
					return;
				showMilliseconds = value;
				view.OnShowMillisecondsChanged();
				if (showTime)
				{
					view.Invalidate();
				}
			}
		}

		public bool ShowRawMessages
		{
			get
			{
				return showRawMessages;
			}
			set
			{
				if (showRawMessages == value)
					return;
				if (showRawMessages && !RawViewAllowed)
					return;
				showRawMessages = value;
				displayFiltersPreprocessingResultCacheIsValid = false;
				highlightFiltersPreprocessingResultCacheIsValid = false;
				InternalUpdate();
				UpdateSelectionInplaceHighlightingFields();
				if (RawViewModeChanged != null)
					RawViewModeChanged(this, EventArgs.Empty);
			}
		}

		public bool RawViewAllowed
		{
			get
			{
				return rawViewAllowed;
			}
			set
			{
				if (rawViewAllowed == value)
					return;
				rawViewAllowed = value;
				if (!rawViewAllowed && ShowRawMessages)
					ShowRawMessages = false;
			}
		}

		public ColoringMode Coloring
		{
			get
			{
				return coloring;
			}
			set
			{
				if (coloring == value)
					return;
				coloring = value;
				view.OnColoringChanged();
			}
		}

		public enum PreferredDblClickAction
		{
			SelectWord,
			DoDefaultAction
		};

		public PreferredDblClickAction DblClickAction { get; set; }

		public enum FocusedMessageDisplayModes
		{
			Master,
			Slave
		};
		public FocusedMessageDisplayModes FocusedMessageDisplayMode { get; set; }
		public MessageBase SlaveModeFocusedMessage
		{
			get 
			{ 
				return slaveModeFocusedMessage; 
			}
			set
			{
				if (value == slaveModeFocusedMessage)
					return;
				slaveModeFocusedMessage = value;
				view.OnSlaveMessageChanged();
			}
		}

		public void SelectSlaveModeFocusedMessage()
		{
			if (displayMessages.Count == 0)
				return;
			var position = FindSlaveModeFocusedMessagePosition(0, displayMessages.Count);
			if (position == null)
				return;
			var idxToSelect = position.Item1;
			if (idxToSelect == displayMessages.Count)
				--idxToSelect;
			SetSelection(idxToSelect, 
				SelectionFlag.SelectBeginningOfLine | SelectionFlag.ShowExtraLinesAroundSelection | SelectionFlag.ScrollToViewEventIfSelectionDidNotChange);
			view.AnimateSlaveMessagePosition();
		}

		static int CompareMessages(MessageBase msg1, MessageBase msg2)
		{
			int ret = MessageTimestamp.Compare(msg1.Time, msg2.Time);
			if (ret != 0)
				return ret;
			ret = Math.Sign(msg1.Position - msg2.Position);
			return ret;
		}

		public Tuple<int, int> FindSlaveModeFocusedMessagePosition(int beginIdx, int endIdx)
		{
			if (slaveModeFocusedMessage == null)
				return null;
			int lowerBound = ListUtils.BinarySearch(displayMessages, beginIdx, endIdx, dm => CompareMessages(dm.DisplayMsg, slaveModeFocusedMessage) < 0);
			int upperBound = ListUtils.BinarySearch(displayMessages, lowerBound, endIdx, dm => CompareMessages(dm.DisplayMsg, slaveModeFocusedMessage) <= 0);
			return new Tuple<int, int>(lowerBound, upperBound);
		}

		public static StringUtils.MultilineText GetTextToDisplay(MessageBase msg, bool showRawMessages)
		{
			if (showRawMessages)
			{
				var r = msg.RawTextAsMultilineText;
				if (r.Text.IsInitialized)
					return r;
				return msg.TextAsMultilineText;
			}
			else
			{
				return msg.TextAsMultilineText;
			}
		}

		public StringUtils.MultilineText GetTextToDisplay(MessageBase msg)
		{
			return GetTextToDisplay(msg, showRawMessages);
		}

		[Flags]
		public enum MessageRectClickFlag
		{
			None = 0,
			RightMouseButton = 1,
			ShiftIsHeld = 2,
			DblClick = 4,
			OulineBoxesAreaClicked = 8,
			AltIsHeld = 16,
		};

		public void MessageRectClicked(
			CursorPosition pos,
			MessageRectClickFlag flags,
			object preparedContextMenuPopupData)
		{
			if ((flags & MessageRectClickFlag.RightMouseButton) != 0)
			{
				if (!selection.IsInsideSelection(pos))
					SetSelection(pos.DisplayIndex, SelectionFlag.None, pos.LineCharIndex);
				view.PopupContextMenu(preparedContextMenuPopupData);
			}
			else
			{
				if ((flags & MessageRectClickFlag.OulineBoxesAreaClicked) != 0)
				{
					if ((flags & MessageRectClickFlag.ShiftIsHeld) == 0)
						SetSelection(pos.DisplayIndex, SelectionFlag.SelectBeginningOfLine | SelectionFlag.NoHScrollToSelection);
					SetSelection(pos.DisplayIndex, SelectionFlag.SelectEndOfLine | SelectionFlag.PreserveSelectionEnd | SelectionFlag.NoHScrollToSelection);
					if ((flags & MessageRectClickFlag.DblClick) != 0)
						PerformDefaultFocusedMessageAction();
				}
				else
				{
					bool defaultSelection = true;
					if ((flags & MessageRectClickFlag.DblClick) != 0)
					{
						PreferredDblClickAction action = DblClickAction;
						if ((flags & MessageRectClickFlag.AltIsHeld) != 0)
							action = PreferredDblClickAction.SelectWord;
						if (action == PreferredDblClickAction.DoDefaultAction)
						{
							PerformDefaultFocusedMessageAction();
							defaultSelection = false;
						}
						else if (action == PreferredDblClickAction.SelectWord)
						{
							defaultSelection = !SelectWordBoundaries(pos);
						}
					}
					if (defaultSelection)
					{
						SetSelection(pos.DisplayIndex, (flags & MessageRectClickFlag.ShiftIsHeld) != 0
							? SelectionFlag.PreserveSelectionEnd : SelectionFlag.None, pos.LineCharIndex);
					}
				}
			}
		}

		public void GoToParentFrame()
		{
			if (selection.Message == null)
				return;
			bool inFrame = false;
			int level = 0;
			IThread focusedThread = selection.Message.Thread;
			IndexedMessage? found = null;
			foreach (IndexedMessage it in loadedMessagesCollection.Reverse(int.MaxValue, int.MinValue, new ShiftPermissions(true, false)))
			{
				if (it.Message.Thread != focusedThread)
					continue;
				if (!inFrame)
				{
					if (it.Message == selection.Message)
						inFrame = true;
				}
				else
				{
					MessageBase.MessageFlag type = it.Message.Flags & MessageBase.MessageFlag.TypeMask;
					if (type == MessageBase.MessageFlag.EndFrame)
					{
						--level;
					}
					else if (type == MessageBase.MessageFlag.StartFrame)
					{
						if (level != 0)
						{
							++level;
						}
						else
						{
							found = it;
							break;
						}
					}
				}
			}
			if (found == null)
			{
				foreach (IndexedMessage it in loadedMessagesCollection.Forward(0, 1))
				{
					SelectOnlyByLoadedMessageIndex(it.Index);
					break;
				}
			}
			SelectFoundMessageHelper(found);
		}

		public void GoToEndOfFrame()
		{
			if (selection.Message == null)
				return;
			bool inFrame = false;
			int level = 0;
			IThread focusedThread = selection.Message.Thread;
			IndexedMessage? found = null;
			foreach (IndexedMessage it in loadedMessagesCollection.Forward(0, int.MaxValue, new ShiftPermissions(false, true)))
			{
				if (it.Message.Thread != focusedThread)
					continue;
				if (!inFrame)
				{
					if (it.Message == selection.Message)
						inFrame = true;
				}
				else
				{
					MessageBase.MessageFlag type = it.Message.Flags & MessageBase.MessageFlag.TypeMask;
					if (type == MessageBase.MessageFlag.StartFrame)
					{
						++level;
					}
					else if (type == MessageBase.MessageFlag.EndFrame)
					{
						if (level != 0)
						{
							--level;
						}
						else
						{
							found = it;
							break;
						}
					}
				}
			}
			if (found == null)
			{
				foreach (IndexedMessage it in loadedMessagesCollection.Reverse(int.MaxValue, -1))
				{
					SelectOnlyByLoadedMessageIndex(it.Index);
					break;
				}
			}
			SelectFoundMessageHelper(found);
		}

		public void GoToNextMessageInThread()
		{
			if (selection.Message == null)
				return;
			IThread focusedThread = selection.Message.Thread;
			bool afterFocused = false;
			IndexedMessage? found = null;
			foreach (IndexedMessage it in loadedMessagesCollection.Forward(0, int.MaxValue, new ShiftPermissions(false, true)))
			{
				if (it.Message.Thread != focusedThread)
					continue;
				if (it.Message.IsHiddenAsFilteredOut)
					continue;
				if (!afterFocused)
				{
					afterFocused = it.Message == selection.Message;
				}
				else
				{
					found = it;
					break;
				}
			}
			SelectFoundMessageHelper(found);
		}

		public void GoToPrevMessageInThread()
		{
			if (selection.Message == null)
				return;
			IThread focusedThread = selection.Message.Thread;
			bool beforeFocused = false;
			IndexedMessage? found = null;
			foreach (IndexedMessage it in loadedMessagesCollection.Reverse(int.MaxValue, int.MinValue, new ShiftPermissions(true, false)))
			{
				if (it.Message.Thread != focusedThread)
					continue;
				if (it.Message.IsHiddenAsFilteredOut)
					continue;
				if (!beforeFocused)
				{
					beforeFocused = it.Message == selection.Message;
				}
				else
				{
					found = it;
					break;
				}
			}
			SelectFoundMessageHelper(found);
		}

		public void GoToNextHighlightedMessage()
		{
			if (selection.Message == null || model.HighlightFilters == null)
				return;
			bool afterFocused = false;
			IndexedMessage? foundMessage = null;
			foreach (IndexedMessage it in loadedMessagesCollection.Forward(0, int.MaxValue, new ShiftPermissions(false, true)))
			{
				if (it.Message.IsHiddenAsFilteredOut)
					continue;
				if (!afterFocused)
				{
					afterFocused = it.Message == selection.Message;
				}
				else if (it.Message.IsHighlighted)
				{
					foundMessage = it;
					break;
				}
			}
			SelectFoundMessageHelper(foundMessage);
		}

		public void GoToPrevHighlightedMessage()
		{
			if (selection.Message == null)
				return;
			IThread focusedThread = selection.Message.Thread;
			bool beforeFocused = false;
			IndexedMessage? found = null;
			foreach (IndexedMessage it in loadedMessagesCollection.Reverse(int.MaxValue, int.MinValue, new ShiftPermissions(true, false)))
			{
				if (it.Message.IsHiddenAsFilteredOut)
					continue;
				if (!beforeFocused)
				{
					beforeFocused = it.Message == selection.Message;
				}
				else if (it.Message.IsHighlighted)
				{
					found = it;
					break;
				}
			}
			SelectFoundMessageHelper(found);
		}

		public void InvalidateTextLineUnderCursor()
		{
			if (selection.First.Message != null)
			{
				view.InvalidateMessage(selection.First.ToDisplayLine());
			}
		}

		public enum SelectionFlag
		{
			None = 0,
			PreserveSelectionEnd = 1,
			SelectBeginningOfLine = 2,
			SelectEndOfLine = 4,
			SelectBeginningOfNextWord = 8,
			SelectBeginningOfPrevWord = 16,
			ShowExtraLinesAroundSelection = 32,
			SuppressOnFocusedMessageChanged = 64,
			NoHScrollToSelection = 128,
			ScrollToViewEventIfSelectionDidNotChange = 256
		};

		public void SetSelection(int displayIndex, SelectionFlag flag = SelectionFlag.None, int? textCharIndex = null)
		{
			using (tracer.NewFrame)
			{
				var dmsg = displayMessages[displayIndex];
				var msg = dmsg.DisplayMsg;
				var line = GetTextToDisplay(msg).GetNthTextLine(dmsg.TextLineIndex);
				int newLineCharIndex;
				if ((flag & SelectionFlag.SelectBeginningOfLine) != 0)
					newLineCharIndex = 0;
				else if ((flag & SelectionFlag.SelectEndOfLine) != 0)
					newLineCharIndex = line.Length;
				else
				{
					newLineCharIndex = Utils.PutInRange(0, line.Length,
						textCharIndex.GetValueOrDefault(selection.First.LineCharIndex));
					if ((flag & SelectionFlag.SelectBeginningOfNextWord) != 0)
						newLineCharIndex = StringUtils.FindNextWordInString(line, newLineCharIndex);
					else if ((flag & SelectionFlag.SelectBeginningOfPrevWord) != 0)
						newLineCharIndex = StringUtils.FindPrevWordInString(line, newLineCharIndex);
				}

				tracer.Info("Selecting line {0}. Display position = {1}", msg.GetHashCode(), displayIndex);

				bool resetEnd = (flag & SelectionFlag.PreserveSelectionEnd) == 0;

				Action doScrolling = () =>
				{
					view.ScrollInView(displayIndex, (flag & SelectionFlag.ShowExtraLinesAroundSelection) != 0);
					if ((flag & SelectionFlag.NoHScrollToSelection) == 0)
						view.HScrollToSelectedText();
					view.RestartCursorBlinking();
				};

				if (selection.First.Message != msg 
					|| selection.First.DisplayIndex != displayIndex 
					|| selection.First.LineCharIndex != newLineCharIndex
					|| resetEnd != selection.IsEmpty)
				{
					var oldSelection = selection;

					InvalidateTextLineUnderCursor();

					var tmp = new CursorPosition() { 
						Message = msg, DisplayIndex = displayIndex, TextLineIndex = dmsg.TextLineIndex, LineCharIndex = newLineCharIndex };

					selection.SetSelection(tmp, resetEnd ? tmp : new CursorPosition?());

					OnSelectionChanged();

					foreach (var displayIndexToInvalidate in oldSelection.GetDisplayIndexesRange().SymmetricDifference(selection.GetDisplayIndexesRange()).Where(idx => idx < displayMessages.Count))
						view.InvalidateMessage(displayMessages[displayIndexToInvalidate].ToDisplayLine(displayIndexToInvalidate));

					InvalidateTextLineUnderCursor();

					doScrolling();

					UpdateSelectionInplaceHighlightingFields();

					if (selection.First.Message != oldSelection.First.Message)
					{
						OnFocusedMessageChanged();
						tracer.Info("Focused line changed to the new selection");
					}
				}
				else if ((flag & SelectionFlag.ScrollToViewEventIfSelectionDidNotChange) != 0)
				{
					doScrolling();
				}
			}
		}

		public void ClearSelection()
		{
			if (selection.First.Message == null)
				return;

			InvalidateTextLineUnderCursor();
			foreach (var displayIndexToInvalidate in selection.GetDisplayIndexesRange().Where(idx => idx < displayMessages.Count))
				view.InvalidateMessage(displayMessages[displayIndexToInvalidate].ToDisplayLine(displayIndexToInvalidate));

			selection.SetSelection(new CursorPosition(), new CursorPosition());
			OnSelectionChanged();
			OnFocusedMessageChanged();
		}

		public string GetSelectedText()
		{
			if (selection.IsEmpty)
				return "";
			StringBuilder sb = new StringBuilder();
			var normSelection = selection.Normalize();
			int selectedLinesCount = normSelection.Last.DisplayIndex - normSelection.First.DisplayIndex + 1;
			foreach (var i in displayMessages.Skip(normSelection.First.DisplayIndex).Take(selectedLinesCount).ZipWithIndex())
			{
				if (i.Key > 0)
					sb.AppendLine();
				var line = GetTextToDisplay(i.Value.DisplayMsg).GetNthTextLine(i.Value.TextLineIndex);
				int beginIdx = i.Key == 0 ? normSelection.First.LineCharIndex : 0;
				int endIdx = i.Key == selectedLinesCount - 1 ? normSelection.Last.LineCharIndex : line.Length;
				line.SubString(beginIdx, endIdx - beginIdx).Append(sb);
			}
			return sb.ToString();
		}

		public void CopySelectionToClipboard()
		{
			var txt = GetSelectedText();
			if (txt.Length > 0)
				view.SetClipboard(txt);
		}

		public void SelectMessageAt(DateTime date, NavigateFlag alignFlag, ILogSource preferredSource)
		{
			using (tracer.NewFrame)
			{
				MessageTimestamp d = new MessageTimestamp(date);

				tracer.Info("Date={0}, alag={1}", d, alignFlag);

				if (displayMessages.Count == 0)
				{
					tracer.Info("No visible messages. Returning");
					return;
				}

				int lowerBound = ListUtils.BinarySearch(displayMessages, 0, displayMessages.Count, x => x.DisplayMsg.Time < d);
				int upperBound = ListUtils.BinarySearch(displayMessages, 0, displayMessages.Count, x => x.DisplayMsg.Time <= d);

				int idx;
				switch (alignFlag & NavigateFlag.AlignMask)
				{
					case NavigateFlag.AlignTop:
						if (upperBound != lowerBound) // if the date in question is present among messages
							idx = lowerBound; // return the first message with this date
						else // ok, no messages with date (d), lowerBound points to the message with date greater than (d)
							idx = lowerBound - 1; // return the message before lowerBound, it will have date less than (d)
						break;
					case NavigateFlag.AlignBottom:
						if (upperBound > lowerBound) // if the date in question is present among messages
							idx = upperBound - 1; // return the last message with date (d)
						else // no messages with date (d), lowerBound points to the message with date greater than (d)
							idx = upperBound; // we bound to return upperBound
						break;
					case NavigateFlag.AlignCenter:
						if (upperBound > lowerBound) // if the date in question is present among messages
						{
							idx = (lowerBound + upperBound - 1) / 2; // return the middle of the range
						}
						else
						{
							// otherwise choose the nearest message

							int p1 = Math.Max(lowerBound - 1, 0);
							int p2 = Math.Min(upperBound, displayMessages.Count - 1);
							MessageBase m1 = displayMessages[p1].DisplayMsg;
							MessageBase m2 = displayMessages[p2].DisplayMsg;
							if (Math.Abs((m1.Time.ToLocalDateTime() - d.ToLocalDateTime()).Ticks) < Math.Abs((m2.Time.ToLocalDateTime() - d.ToLocalDateTime()).Ticks))
							{
								idx = p1;
							}
							else
							{
								idx = p2;
							}
						}
						break;
					default:
						throw new ArgumentException();
				}

				int maxIdx = displayMessages.Count - 1;
				int minIdx = 0;

				idx = Utils.PutInRange(minIdx, maxIdx, idx);

				if (preferredSource != null)
				{
					int? foundIdx = null;
					Func<int, bool> tryShift = shift =>
					{
						int newIdx = Utils.PutInRange(minIdx, maxIdx, idx + shift);
						if (displayMessages[newIdx].DisplayMsg.LogSource == preferredSource)
							foundIdx = newIdx;
						return foundIdx.HasValue;
					};
					int maxShift = 1000;
					for (int shift = 0; shift < maxShift; ++shift)
						if (tryShift(shift) || tryShift(-shift))
							break;
					if (foundIdx.HasValue)
						idx = foundIdx.Value;
				}

				tracer.Info("Index of the line to be selected: {0}", idx);

				SetSelection(idx, SelectionFlag.SelectBeginningOfLine | SelectionFlag.ShowExtraLinesAroundSelection);
			}
		}

		public SearchResult Search(SearchOptions opts)
		{
			// init return visible with default visible (rv.Succeeded = false)
			SearchResult rv = new SearchResult();

			opts.CoreOptions.SearchInRawText = showRawMessages;
			bool emptyTemplate = string.IsNullOrEmpty(opts.CoreOptions.Template);
			bool reverseSearch = opts.CoreOptions.ReverseSearch;

			CursorPosition startFrom = new CursorPosition();
			var normSelection = selection.Normalize();
			if (!reverseSearch)
			{
				var tmp = normSelection.Last;
				if (tmp.Message == null)
					tmp = normSelection.First;
				if (tmp.Message != null)
				{
					startFrom = tmp;
				}
			}
			else
			{
				if (normSelection.First.Message != null)
				{
					startFrom = normSelection.First;
				}
			}

			int startFromMessage = 0;
			int startFromTextPosition = 0;

			if (startFrom.Message != null)
			{
				int? startFromMessageOpt = DisplayIndex2LoadedMessageIndex(startFrom.DisplayIndex);
				if (startFromMessageOpt.HasValue)
				{
					startFromMessage = startFromMessageOpt.Value;
					var startLine = GetTextToDisplay(startFrom.Message).GetNthTextLine(startFrom.TextLineIndex);
					startFromTextPosition = (startLine.StartIndex - GetTextToDisplay(startFrom.Message).Text.StartIndex) + startFrom.LineCharIndex;
				}
			}

			Search.PreprocessedOptions preprocessedOptions = opts.CoreOptions.Preprocess();
			Search.BulkSearchState bulkSearchState = new Search.BulkSearchState();
			
			int messagesProcessed = 0;

			foreach (IndexedMessage it in MakeSearchScope(opts.CoreOptions.WrapAround, reverseSearch, startFromMessage))
			{
				if (opts.SearchOnlyWithinFirstMessage && messagesProcessed == 1)
					break;

				++messagesProcessed;

				MessageBase.MessageFlag f = it.Message.Flags;

				if ((f & (MessageBase.MessageFlag.HiddenBecauseOfInvisibleThread | MessageBase.MessageFlag.HiddenAsFilteredOut)) != 0)
					continue; // dont search in excluded lines

				int? startFromTextPos = null;
				if (!emptyTemplate && messagesProcessed == 1)
					startFromTextPos = startFromTextPosition;

				var match = LogJoint.Search.SearchInMessageText(it.Message, preprocessedOptions, bulkSearchState, startFromTextPos);

				if (!match.HasValue)
					continue;

				if (emptyTemplate && messagesProcessed == 1)
					continue;

				// init successful return visible
				rv.Succeeded = true;
				rv.Position = it.Index;
				rv.Message = it.Message;

				EnsureNotCollapsed(it.Index);

				int? displayIndex = LoadedIndex2DisplayIndex(it.Index);

				if (displayIndex.HasValue)
				{
					if (opts.HighlightResult)
					{
						var txt = GetTextToDisplay(it.Message).Text;
						var m = match.Value;
						GetTextToDisplay(it.Message).EnumLines((line, lineIdx) =>
						{
							var lineBegin = line.StartIndex - txt.StartIndex;
							var lineEnd = lineBegin + line.Length;
							if (m.MatchBegin >= lineBegin && m.MatchBegin <= lineEnd)
								SetSelection(displayIndex.Value + lineIdx, SelectionFlag.ShowExtraLinesAroundSelection, m.MatchBegin - lineBegin);
							if (m.MatchEnd >= lineBegin && m.MatchEnd <= lineEnd)
								SetSelection(displayIndex.Value + lineIdx, SelectionFlag.PreserveSelectionEnd | SelectionFlag.ShowExtraLinesAroundSelection, 
									m.MatchEnd - lineBegin);
							return true;
						});
					}
					else
					{
						SetSelection(displayIndex.Value, SelectionFlag.SelectBeginningOfLine | SelectionFlag.ShowExtraLinesAroundSelection);
					}
				}

				view.HScrollToSelectedText();

				view.Invalidate();

				break;
			}

			if (!rv.Succeeded && selection.Message == null)
				MoveSelection(reverseSearch ? 0 : displayMessages.Count - 1, 
					MoveSelectionFlag.ForwardShiftingMode, 
					SelectionFlag.ShowExtraLinesAroundSelection);

			// return visible initialized by-default as non-successful search

			return rv;
		}

		public enum MoveSelectionFlag
		{
			None = 0,
			ForwardShiftingMode = 1,
			BackwardShiftingMode = 2
		};

		public void MoveSelection(int newDisplayPosition, MoveSelectionFlag moveFlags, SelectionFlag selFlags)
		{
			bool forwardMode = (moveFlags & MoveSelectionFlag.BackwardShiftingMode) == 0;
			using (var shifter = displayMessagesCollection.CreateShifter(GetShiftPermissions()))
			{
				foreach (var l in
					forwardMode ?
						displayMessagesCollection.Forward(newDisplayPosition, int.MaxValue, shifter) :
						displayMessagesCollection.Reverse(newDisplayPosition, int.MinValue, shifter)
				)
				{
					SetSelection(l.Index, selFlags);
					break;
				}
			}
		}

		public void Next()
		{
			MoveSelection(selection.DisplayPosition + 1, 
				MoveSelectionFlag.BackwardShiftingMode,
				SelectionFlag.ShowExtraLinesAroundSelection);
		}

		public void Prev()
		{
			MoveSelection(selection.DisplayPosition - 1, 
				MoveSelectionFlag.ForwardShiftingMode,
				SelectionFlag.ShowExtraLinesAroundSelection);
		}


		public DateTime? FocusedMessageTime
		{
			get
			{
				if (selection.Message != null)
					return selection.Message.Time.ToLocalDateTime();
				return null;
			}
		}

		public MessageBase FocusedMessage
		{
			get
			{
				return selection.Message;
			}
		}

		public LoadedMessagesCollection LoadedMessages
		{
			get { return loadedMessagesCollection; }
		}

		public int? DisplayIndex2LoadedMessageIndex(int dposition)
		{
			MessageBase l = displayMessages[dposition].DisplayMsg;
			int lower = ListUtils.BinarySearch(mergedMessages, 0, mergedMessages.Count, e => e.LoadedMsg.Time < l.Time);
			int upper = ListUtils.BinarySearch(mergedMessages, 0, mergedMessages.Count, e => e.LoadedMsg.Time <= l.Time);
			for (int i = lower; i < upper; ++i)
				if (mergedMessages[i].LoadedMsg == l)
					return i;
			return null;
		}

		public IBookmark NextBookmark(bool forward)
		{
			if (selection.Message == null || model.Bookmarks == null)
				return null;
			return model.Bookmarks.GetNext(selection.Message, forward, this);
		}

		[Flags]
		public enum BookmarkSelectionStatus
		{
			Success = 0,
			BookmarkedMessageNotFound = 1,
			BookmarkedMessageIsFilteredOut = 2,
			BookmarkedMessageIsHiddenBecauseOfInvisibleThread = 4
		};

		public BookmarkSelectionStatus SelectMessageAt(IBookmark bmk)
		{
			return SelectMessageAt(bmk, null);
		}

		public BookmarkSelectionStatus SelectMessageAt(IBookmark bmk, Predicate<MessageBase> messageMatcherWhenNoHashIsSpecified)
		{
			if (bmk == null)
				return BookmarkSelectionStatus.BookmarkedMessageNotFound;

			int begin;
			int end;
			GetDateEqualRange(bmk.Time, out begin, out end);

			if (begin == end)
			{
				// If we are at the boundary positions
				if (end == 0 || begin == mergedMessages.Count)
				{
					// We can try to load the messages around the time of the bookmark
					OnBeginShifting();
					model.ShiftAt(bmk.Time.ToLocalDateTime());
					OnEndShifting();

					// Refresh the lines. The messages are expected to be changed.
					this.InternalUpdate();

					// Find a new equal range
					GetDateEqualRange(bmk.Time, out begin, out end);
				}
				else
				{
					// We found empty equal range of bookmark's time,
					// this range points to the middle of mergedMessages array.
					// That means that the bookmark is invalid: it points 
					// into the time that doesn't exist among messages.
				}
			}

			if (bmk.MessageHash != 0)
			{
				// Search for the bookmarked message by hash
				for (; begin != end; ++begin)
				{
					MessageBase l = mergedMessages[begin].LoadedMsg;
					if (l.GetHashCode() == bmk.MessageHash)
						break;
				}
			}
			else if (messageMatcherWhenNoHashIsSpecified != null)
			{
				// Search for the bookmarked message by user provided criteria
				for (; begin != end; ++begin)
				{
					MessageBase l = mergedMessages[begin].LoadedMsg;
					if (messageMatcherWhenNoHashIsSpecified(l))
						break;
				}
			}
			else
			{
				begin = end;
			}

			if (begin == end)
				return BookmarkSelectionStatus.BookmarkedMessageNotFound;
			
			MessageBase bookmarkedMessage = mergedMessages[begin].LoadedMsg;
			BookmarkSelectionStatus status = BookmarkSelectionStatus.Success;
			if (bookmarkedMessage.IsHiddenAsFilteredOut)
				status |= BookmarkSelectionStatus.BookmarkedMessageIsFilteredOut;
			if (bookmarkedMessage.IsHiddenBecauseOfInvisibleThread)
				status |= BookmarkSelectionStatus.BookmarkedMessageIsHiddenBecauseOfInvisibleThread;
			if (status != BookmarkSelectionStatus.Success)
				return status;

			EnsureNotCollapsed(begin);

			int? idx = LoadedIndex2DisplayIndex(begin);
			if (idx == null)
				return BookmarkSelectionStatus.BookmarkedMessageNotFound; // is it possible?

			SetSelection(idx.Value, SelectionFlag.SelectEndOfLine | SelectionFlag.ShowExtraLinesAroundSelection | SelectionFlag.NoHScrollToSelection);
			SetSelection(idx.Value, SelectionFlag.SelectBeginningOfLine | SelectionFlag.PreserveSelectionEnd);
			return BookmarkSelectionStatus.Success;
		}

		public bool DoExpandCollapse(MessageBase line, bool recursive, bool? collapse)
		{
			using (tracer.NewFrame)
			{
				FrameBegin fb = line as FrameBegin;
				if (fb == null)
					return false;

				if (!recursive && collapse.HasValue)
					if (collapse.Value == fb.Collapsed)
						return false;

				fb.Collapsed = !fb.Collapsed;
				if (recursive)
					foreach (MessageBase i in EnumFrameContent(fb))
					{
						FrameBegin fb2 = i as FrameBegin;
						if (fb2 != null)
							fb2.Collapsed = fb.Collapsed;
					}

				InternalUpdate();

				return true;
			}
		}

		public void CollapseOrExpandAllFrames(bool collapse)
		{
			bool atLeastOneChanged = false;
			foreach (IndexedMessage il in loadedMessagesCollection.Forward(0, int.MaxValue))
			{
				MessageBase.MessageFlag f = il.Message.Flags;
				if ((f & MessageBase.MessageFlag.StartFrame) == 0)
					continue;
				bool alreadyCollapsed = (f & MessageBase.MessageFlag.Collapsed) != 0;
				if (collapse == alreadyCollapsed)
					continue;
				atLeastOneChanged = true;
				((FrameBegin)il.Message).Collapsed = collapse;
			}
			if (atLeastOneChanged)
			{
				InternalUpdate();
			}
		}

		public IEnumerable<MessageBase> EnumFrameContent(FrameBegin fb)
		{
			bool inFrame = false;
			foreach (IndexedMessage il in loadedMessagesCollection.Forward(0, int.MaxValue))
			{
				if (il.Message.Thread != fb.Thread)
					continue;
				bool stop = false;
				if (!inFrame)
					inFrame = il.Message == fb;
				if (il.Message == fb.End)
					stop = true;
				if (inFrame)
					yield return il.Message;
				if (stop)
					break;
			}
		}

		public void EnsureNotCollapsed(int loadedMessageIndex)
		{
			using (IEnumerator<IndexedMessage> it = loadedMessagesCollection.Reverse(loadedMessageIndex, -1).GetEnumerator())
			{
				if (!it.MoveNext())
					return;
				IThread thread = it.Current.Message.Thread;
				int frameCount = 0;
				for (; it.MoveNext(); )
				{
					if (it.Current.Message.Thread != thread)
						continue;
					MessageBase.MessageFlag f = it.Current.Message.Flags;
					switch (f & MessageBase.MessageFlag.TypeMask)
					{
						case MessageBase.MessageFlag.StartFrame:
							if (frameCount == 0)
								DoExpandCollapse(it.Current.Message, false, false);
							else
								--frameCount;
							break;
						case MessageBase.MessageFlag.EndFrame:
							++frameCount;
							break;
					}
				}
			}
		}

		bool SelectOnlyByLoadedMessageIndex(int loadedMessageIndex)
		{
			EnsureNotCollapsed(loadedMessageIndex);
			int? displayIndex = LoadedIndex2DisplayIndex(loadedMessageIndex);
			if (!displayIndex.HasValue)
				return false;
			SetSelection(displayIndex.Value, SelectionFlag.SelectBeginningOfLine | SelectionFlag.ShowExtraLinesAroundSelection);
			return true;
		}


		public void SelectFirstMessage()
		{
			using (tracer.NewFrame)
			{
				MoveSelection(0,
					MoveSelectionFlag.ForwardShiftingMode, SelectionFlag.SelectBeginningOfLine | SelectionFlag.ShowExtraLinesAroundSelection);
			}
		}

		public void SelectLastMessage()
		{
			using (tracer.NewFrame)
			{
				MoveSelection(displayMessages.Count - 1,
					MoveSelectionFlag.BackwardShiftingMode, SelectionFlag.SelectBeginningOfLine | SelectionFlag.ShowExtraLinesAroundSelection);
			}
		}

		public void ShiftHome()
		{
			using (tracer.NewFrame)
			{
				model.ShiftHome();
			}
		}

		public void ShiftToEnd()
		{
			using (tracer.NewFrame)
			{
				model.ShiftToEnd();
			}
		}

		public bool BookmarksAvailable
		{
			get { return model.Bookmarks != null; }
		}

		public void ToggleBookmark(MessageBase line)
		{
			if (model.Bookmarks == null)
				return;
			model.Bookmarks.ToggleBookmark(line);
			InternalUpdate();
		}

		public void PerformDefaultFocusedMessageAction()
		{
			if (DefaultFocusedMessageAction != null)
				DefaultFocusedMessageAction(this, EventArgs.Empty);
		}

		public abstract class MessagesCollection : IMessagesCollection
		{
			protected readonly Presenter control;

			public MessagesCollection(Presenter control)
			{
				this.control = control;
			}

			#region IMessagesCollection

			public abstract int Count { get; }

			public IEnumerable<IndexedMessage> Forward(int begin, int end)
			{
				return Forward(begin, end, new ShiftPermissions());
			}

			public IEnumerable<IndexedMessage> Reverse(int begin, int end)
			{
				return Reverse(begin, end, new ShiftPermissions());
			}

			#endregion

			public IEnumerable<IndexedMessage> Forward(int begin, int end, ShiftPermissions shiftPerm)
			{
				using (Shifter shifter = CreateShifter(shiftPerm))
				{
					foreach (var m in Forward(begin, end, shifter))
						yield return m;
				}
			}

			public IEnumerable<IndexedMessage> Reverse(int begin, int end, ShiftPermissions shiftPerm)
			{
				using (Shifter shifter = CreateShifter(shiftPerm))
				{
					foreach (var m in Reverse(begin, end, shifter))
						yield return m;
				}
			}

			internal Shifter CreateShifter(ShiftPermissions shiftPerm)
			{
				return new Shifter(control, this, shiftPerm);
			}

			internal abstract IEnumerable<IndexedMessage> Forward(int begin, int end, Shifter shifter);
			internal abstract IEnumerable<IndexedMessage> Reverse(int begin, int end, Shifter shifter);
		};

		public class LoadedMessagesCollection : MessagesCollection
		{
			public LoadedMessagesCollection(Presenter control): base(control)
			{
			}

			public override int Count
			{
				get { return control.mergedMessages.Count; }
			}

			internal override IEnumerable<IndexedMessage> Forward(int begin, int end, Shifter shifter)
			{
				shifter.InitialValidatePositions(ref begin, ref end, false);
				for (; begin < end; ++begin)
				{
					if (!shifter.ValidatePositions(ref begin, ref end, false))
						yield break;
					yield return new IndexedMessage(begin, control.mergedMessages[begin].LoadedMsg);
				}
			}

			internal override IEnumerable<IndexedMessage> Reverse(int begin, int end, Shifter shifter)
			{
				shifter.InitialValidatePositions(ref begin, ref end, true);

				for (; begin > end; --begin)
				{
					if (!shifter.ValidatePositions(ref begin, ref end, true))
						yield break;
					yield return new IndexedMessage(begin, control.mergedMessages[begin].LoadedMsg);
				}
			}
		};

		public class DisplayMessagesCollection : MessagesCollection
		{
			public DisplayMessagesCollection(Presenter control)
				: base(control)
			{
			}

			public override int Count
			{
				get { return control.displayMessages.Count; }
			}

			public int BinarySearch(int begin, int end, Predicate<MessageBase> lessThanValudBeingSearched)
			{
				return ListUtils.BinarySearch(control.displayMessages, begin, end, 
					dm => lessThanValudBeingSearched(dm.DisplayMsg));
			}

			internal struct IndexedDisplayMessage
			{
				public int Index;
				public MessageBase Message;
				public int TextLineIndex;
				public IndexedDisplayMessage(int index, MessageBase message, int textLineIndex)
				{
					Index = index;
					Message = message;
					TextLineIndex = textLineIndex;
				}

				public IndexedMessage ToIndexedMessage() { return new IndexedMessage(Index, Message); }
			};

			internal IEnumerable<IndexedDisplayMessage> ForwardInternal(int begin, int end, Shifter shifter)
			{
				shifter.InitialValidatePositions(ref begin, ref end, false);
				for (; begin < end; ++begin)
				{
					if (!shifter.ValidatePositions(ref begin, ref end, false))
						yield break;
					var displayMsg = control.displayMessages[begin];
					yield return new IndexedDisplayMessage(begin, displayMsg.DisplayMsg, displayMsg.TextLineIndex);
				}
			}

			internal override IEnumerable<IndexedMessage> Forward(int begin, int end, Shifter shifter)
			{
				return ForwardInternal(begin, end, shifter).Select(i => i.ToIndexedMessage());
			}

			internal IEnumerable<IndexedDisplayMessage> ReverseInternal(int begin, int end, Shifter shifter)
			{
				shifter.InitialValidatePositions(ref begin, ref end, true);
				for (; begin > end; --begin)
				{
					if (!shifter.ValidatePositions(ref begin, ref end, true))
						yield break;
					var displayMsg = control.displayMessages[begin];
					yield return new IndexedDisplayMessage(begin, displayMsg.DisplayMsg, displayMsg.TextLineIndex);
				}
			}

			internal override IEnumerable<IndexedMessage> Reverse(int begin, int end, Shifter shifter)
			{
				return ReverseInternal(begin, end, shifter).Select(i => i.ToIndexedMessage());
			}
		};

		public ShiftPermissions GetShiftPermissions()
		{
			return new ShiftPermissions(selection.DisplayPosition == 0, selection.DisplayPosition == (displayMessages.Count - 1));
		}

		public void OnShowFiltersClicked()
		{
			if (navHandler != null)
				navHandler.ShowFiltersView();
		}

		public enum Key
		{
			None,
			F5,
			Up, Down, Left, Right,
			PageUp, PageDown,
			Apps, 
			Enter,
			Copy,
			Home,
			End
		};

		public void KeyPressed(Key k, bool ctrl, bool alt, bool shift)
		{
			Presenter.SelectionFlag preserveSelectionFlag = shift ? SelectionFlag.PreserveSelectionEnd : SelectionFlag.None;

			if (k == Key.F5)
			{
				OnRefresh();
				return;
			}

			CursorPosition cur = selection.First;

			if (selection.Message != null)
			{
				if (k == Key.Up)
					if (ctrl)
						GoToParentFrame();
					else if (alt)
						GoToPrevMessageInThread();
					else
						MoveSelection(selection.DisplayPosition - 1, MoveSelectionFlag.ForwardShiftingMode, preserveSelectionFlag);
				else if (k == Key.Down)
					if (ctrl)
						GoToEndOfFrame();
					else if (alt)
						GoToNextMessageInThread();
					else
						MoveSelection(selection.DisplayPosition + 1, MoveSelectionFlag.BackwardShiftingMode, preserveSelectionFlag);
				else if (k == Key.PageUp)
					MoveSelection(selection.DisplayPosition - view.DisplayLinesPerPage, Presenter.MoveSelectionFlag.ForwardShiftingMode,
							preserveSelectionFlag);
				else if (k == Key.PageDown)
					MoveSelection(selection.DisplayPosition + view.DisplayLinesPerPage, Presenter.MoveSelectionFlag.BackwardShiftingMode,
							preserveSelectionFlag);
				else if (k == Key.Left || k == Key.Right)
				{
					var left = k == Key.Left;
					if (!DoExpandCollapse(selection.Message, ctrl, left))
					{
						if (ctrl)
						{
							var wordFlag = left ? Presenter.SelectionFlag.SelectBeginningOfPrevWord : Presenter.SelectionFlag.SelectBeginningOfNextWord;
							SetSelection(cur.DisplayIndex, preserveSelectionFlag | wordFlag, cur.LineCharIndex);
						}
						else
						{
							SetSelection(cur.DisplayIndex, preserveSelectionFlag, cur.LineCharIndex + (left ? -1 : +1));
						}
						if (selection.First.LineCharIndex == cur.LineCharIndex)
						{
							MoveSelection(
								selection.DisplayPosition + (left ? -1 : +1),
								left ? Presenter.MoveSelectionFlag.ForwardShiftingMode : Presenter.MoveSelectionFlag.BackwardShiftingMode,
								preserveSelectionFlag | (left ? Presenter.SelectionFlag.SelectEndOfLine : Presenter.SelectionFlag.SelectBeginningOfLine)
							);
						}
					}
				}
				else if (k == Key.Apps)
					view.PopupContextMenu(view.GetContextMenuPopupDataForCurrentSelection());
				else if (k == Key.Enter)
					PerformDefaultFocusedMessageAction();
			}
			if (k == Key.Copy)
			{
				CopySelectionToClipboard();
			}
			if (k == Key.Home)
			{
				SetSelection(cur.DisplayIndex, preserveSelectionFlag | Presenter.SelectionFlag.SelectBeginningOfLine);
				if (ctrl && !GetShiftPermissions().AllowUp)
				{
					ShiftHome();
					SelectFirstMessage();
				}
			}
			else if (k == Key.End)
			{
				if (ctrl && !GetShiftPermissions().AllowDown)
				{
					ShiftToEnd();
					SelectLastMessage();
				}
				SetSelection(selection.First.DisplayIndex, preserveSelectionFlag | Presenter.SelectionFlag.SelectEndOfLine);
			}
		}

		#endregion

		#region Protected methods

		protected virtual void OnFocusedMessageChanged()
		{
			if (FocusedMessageChanged != null)
				FocusedMessageChanged(this, EventArgs.Empty);
		}

		protected virtual void OnSelectionChanged()
		{
			if (SelectionChanged != null)
				SelectionChanged(this, EventArgs.Empty);
		}

		protected virtual void OnBeginShifting()
		{
			if (BeginShifting != null)
				BeginShifting(this, EventArgs.Empty);
		}

		protected virtual void OnEndShifting()
		{
			if (EndShifting != null)
				EndShifting(this, EventArgs.Empty);
		}

		protected virtual void OnRefresh()
		{
			if (ManualRefresh != null)
				ManualRefresh(this, EventArgs.Empty);
		}

		#endregion

		#region Implementation

		struct MergedMessagesEntry
		{
			public MessageBase LoadedMsg;
			public FiltersList.PreprocessingResult DisplayFiltersPreprocessingResult;
			public FiltersList.PreprocessingResult HighlightFiltersPreprocessingResult;
		};

		struct DisplayMessagesEntry
		{
			public MessageBase DisplayMsg;
			public int TextLineIndex;
			public DisplayLine ToDisplayLine(int index)
			{
				return new DisplayLine()
				{
					Message = DisplayMsg,
					DisplayLineIndex = index,
					TextLineIndex = TextLineIndex
				};
			}
		};

		public struct ShiftPermissions
		{
			public readonly bool AllowUp;
			public readonly bool AllowDown;
			public ShiftPermissions(bool allowUp, bool allowDown)
			{
				this.AllowUp = allowUp;
				this.AllowDown = allowDown;
			}
		};

		internal class Shifter : IDisposable
		{
			bool active;
			IMessagesCollection collection;
			Presenter control;
			ShiftPermissions shiftPerm;

			public Shifter(Presenter control, IMessagesCollection collection, ShiftPermissions shiftPerm)
			{
				this.control = control;
				this.shiftPerm = shiftPerm;
				this.collection = collection;
			}

			public void InitialValidatePositions(ref int begin, ref int end, bool reverse)
			{
				if (!reverse)
				{
					if (!shiftPerm.AllowUp)
						begin = Math.Max(0, begin);
					if (!shiftPerm.AllowDown)
						end = Math.Min(collection.Count, end);
				}
				else
				{
					if (!shiftPerm.AllowUp)
						end = Math.Max(-1, end);
					if (!shiftPerm.AllowDown)
						begin = Math.Min(collection.Count - 1, begin);
				}
			}

			public bool ValidatePositions(ref int begin, ref int end, bool reverse)
			{
				if (shiftPerm.AllowUp)
				{
					while (begin < 0)
					{
						int delta = ShiftUp();
						if (delta == 0)
							return false;
						end += delta;
						begin += delta;
					}
				}
				if (shiftPerm.AllowDown)
				{
					while (begin >= collection.Count)
					{
						int delta = ShiftDown();
						if (delta == 0)
							return false;
						end -= delta;
						begin -= delta;
					}
				}
				return true;
			}

			public int ShiftDown()
			{
				// Check if it is possible to shift down. Return immediately if not.
				// Just to avoid annoying flickering.
				if (!control.model.IsShiftableDown)
					return 0;

				// Parameters of the current last message
				long lastPosition = 0; // hash
				int lastIndex = -1; // display index

				MessageBase lastMessage = null;

				int countBeforeShifting = collection.Count;

				// Get the info about the last (pivot) message.
				// Later after actual shifting it will be used to find the position 
				// where to start the iterating from.
				foreach (IndexedMessage l in collection.Reverse(int.MaxValue, -1))
				{
					lastPosition = l.Message.Position;
					lastIndex = l.Index;
					lastMessage = l.Message;
					break;
				}

				// That can happen if there are no messages loaded yet. Shifting cannot be done.
				if (lastIndex < 0)
					return 0;

				// Start shifting it has not been started yet
				EnsureActive();

				// Ask the host to do actual shifting
				control.model.ShiftDown();

				// Remerge the messages. 
				control.EnsureViewUpdated();

				// Check if the last message has changed. 
				// It is an optimization for the case when there is no room to shift.
				foreach (IndexedMessage l in collection.Reverse(int.MaxValue, -1))
				{
					if (lastPosition == l.Message.Position)
						return 0;
					break;
				}

				// Search for the pivot message among new merged collection
				foreach (IndexedMessage l in collection.Forward(0, int.MaxValue))
					if (l.Message.Position == lastPosition)
						return lastIndex - l.Index;

				// We didn't find our pivot message after the shifting.
				return 0;
			}

			public int ShiftUp()
			{
				// This function is symmetric to ShiftDown(). See comments there.

				if (!control.model.IsShiftableUp)
					return 0;

				long firstPosition = 0;
				int firstIndex = -1;

				int countBeforeShifting = collection.Count;

				foreach (IndexedMessage l in collection.Forward(0, 1))
				{
					firstPosition = l.Message.Position;
					firstIndex = l.Index;
					break;
				}

				if (firstIndex < 0)
					return 0;

				EnsureActive();

				control.model.ShiftUp();

				control.EnsureViewUpdated();

				foreach (IndexedMessage l in collection.Forward(0, 1))
				{
					if (firstPosition == l.Message.Position)
						return 0;
					break;
				}

				foreach (IndexedMessage l in collection.Reverse(int.MaxValue, -1))
					if (l.Message.Position == firstPosition)
						return l.Index - firstIndex;

				return 0;
			}

			public void ShiftHome()
			{
				if (control.model.IsShiftableUp)
					EnsureActive();
				control.model.ShiftHome();
				control.EnsureViewUpdated();
			}

			public void ShiftToEnd()
			{
				if (control.model.IsShiftableDown)
					EnsureActive();
				control.model.ShiftToEnd();
				control.EnsureViewUpdated();
			}

			void EnsureActive()
			{
				if (active)
					return;
				active = true;
				control.OnBeginShifting();
			}

			#region IDisposable Members

			public void Dispose()
			{
				if (!active)
					return;
				control.OnEndShifting();
			}

			#endregion
		};

		static MergedMessagesEntry InitMergedMessagesEntry(
			MessageBase message, 
			MergedMessagesEntry cachedMessageEntry,
			ThreadLocal<IFiltersList> displayFilters,
			ThreadLocal<IFiltersList> highlighFilters,
			bool displayFiltersPreprocessingResultCacheIsValid,
			bool highlightFiltersPreprocessingResultCacheIsValid,
			bool matchRawMessages)
		{
			MergedMessagesEntry ret;

			ret.LoadedMsg = message;

			//displayFiltersPreprocessingResultCacheIsValid = false;

			if (displayFiltersPreprocessingResultCacheIsValid)
				ret.DisplayFiltersPreprocessingResult = cachedMessageEntry.DisplayFiltersPreprocessingResult;
			else
				ret.DisplayFiltersPreprocessingResult = displayFilters.Value.PreprocessMessage(message, matchRawMessages);

			if (highlightFiltersPreprocessingResultCacheIsValid)
				ret.HighlightFiltersPreprocessingResult = cachedMessageEntry.HighlightFiltersPreprocessingResult;
			else
				ret.HighlightFiltersPreprocessingResult = highlighFilters.Value.PreprocessMessage(message, matchRawMessages);

			return ret;
		}

		static IEnumerable<IndexedMessage> ToEnumerable(IEnumerator<IndexedMessage> enumerator, int count)
		{
			for (; ; )
			{
				if (count == 0)
					break;
				if (enumerator.Current.Message == null)
					enumerator.ToString();
				yield return enumerator.Current;
				--count;
				if (count == 0)
					break;
				enumerator.MoveNext();
			}
		}

		struct FocusedMessageFinder
		{
			Presenter presenter;
			SelectionInfo prevFocused;
			long prevFocusedPosition1;
			ILogSource prevFocusedSource1;
			long prevFocusedPosition2;
			ILogSource prevFocusedSource2;
			IndexedMessage newFocused1;
			IndexedMessage newFocused2;

			public FocusedMessageFinder(Presenter presenter)
			{
				this.presenter = presenter;
				prevFocused = presenter.selection;
				prevFocusedPosition1 = prevFocused.First.Message != null ? prevFocused.First.Message.Position : long.MinValue;
				prevFocusedSource1 = prevFocused.First.Message != null ? prevFocused.First.Message.LogSource : null;
				prevFocusedPosition2 = prevFocused.Last.Message != null ? prevFocused.Last.Message.Position : long.MinValue;
				prevFocusedSource2 = prevFocused.Last.Message != null ? prevFocused.Last.Message.LogSource : null;
				newFocused1 = new IndexedMessage();
				newFocused2 = new IndexedMessage();
			}
			public void HandleNewDisplayMessage(MessageBase msg, int displayIndex)
			{
				if (prevFocusedPosition1 == msg.Position && msg.LogSource == prevFocusedSource1)
					newFocused1 = new IndexedMessage() { Message = msg, Index = displayIndex };
				if (prevFocusedPosition2 == msg.Position && msg.LogSource == prevFocusedSource2)
					newFocused2 = new IndexedMessage() { Message = msg, Index = displayIndex };
			}
			private int ComposeNewDisplayIndex(int newBaseMessageIndex, int originalTextLineIndex)
			{
				var dmsg = presenter.displayMessages[newBaseMessageIndex].DisplayMsg;
				var currentLinesCount = presenter.GetTextToDisplay(dmsg).GetLinesCount();
				
				// when switching raw/normal views the amount of display lines for one MessageBase may change.
				// make sure line index is still valid.
				var newLineIndex = Math.Min(originalTextLineIndex, currentLinesCount - 1);

				return newBaseMessageIndex + newLineIndex;
			}
			public void SetFoundSelection()
			{
				if (newFocused2.Message != null)
				{
					presenter.SetSelection(
						ComposeNewDisplayIndex(newFocused2.Index, prevFocused.Last.TextLineIndex),
						SelectionFlag.SuppressOnFocusedMessageChanged, prevFocused.Last.LineCharIndex);
				}
				if (newFocused1.Message != null)
				{
					presenter.SetSelection(
						ComposeNewDisplayIndex(newFocused1.Index, prevFocused.First.TextLineIndex),
						SelectionFlag.PreserveSelectionEnd, prevFocused.First.LineCharIndex);
				}
				else
				{
					if (prevFocused.First.Message != null)
					{
						var prevFocusedMsg = prevFocused.First.Message;
						int messageNearestToPrevSelection = presenter.DisplayMessages.BinarySearch(0, presenter.DisplayMessages.Count,
							dm => MessagesContainers.MergeCollection.MessagesComparer.Compare(dm, prevFocusedMsg, false) < 0);
						if (messageNearestToPrevSelection != presenter.DisplayMessages.Count)
						{
							presenter.SetSelection(messageNearestToPrevSelection, SelectionFlag.SelectBeginningOfLine);
						}
						else
						{
							presenter.ClearSelection();
						}
					}
					else
					{
						presenter.ClearSelection();
					}
				}

				if (presenter.selection.First.Message != null)
				{
					if (prevFocused.First.Message == null)
					{
						presenter.view.ScrollInView(presenter.selection.First.DisplayIndex, true);
					}
				}
			}
		};

		void InternalUpdate()
		{
			using (tracer.NewFrame)
			using (new ScopedGuard(view.UpdateStarted, view.UpdateFinished))
			using (var threadsBulkProcessing = model.Threads.StartBulkProcessing())
			{
				++mergedMessagesVersion;

				var focusedMessageFinder = new FocusedMessageFinder(this);
				
				int modelMessagesCount = model.Messages.Count;

				ResizeMergedMessages(modelMessagesCount);
				UpdateDisplayMessagesCapacity(modelMessagesCount);

				int loadedCount = 0;

				IFiltersList displayFilters = model.DisplayFilters;
				FiltersList.BulkProcessingHandle displayFiltersProcessingHandle = BeginBulkProcessing(displayFilters);

				IFiltersList hlFilters = model.HighlightFilters;
				FiltersList.BulkProcessingHandle hlFiltersProcessingHandle = BeginBulkProcessing(hlFilters);

				IBookmarksHandler bmk = CreateBookmarksHandler();

				using (var enumerator = model.Messages.Forward(0, int.MaxValue).GetEnumerator())
				using (ThreadLocal<IFiltersList> displayFiltersThreadLocal = new ThreadLocal<IFiltersList>(() => displayFilters.Clone()))
				using (ThreadLocal<IFiltersList> highlightFiltersThreadLocal = new ThreadLocal<IFiltersList>(() => hlFilters.Clone()))
				{
					enumerator.MoveNext();
					foreach (MergedMessagesEntry preprocessedMessage in 
						ToEnumerable(enumerator, modelMessagesCount)
						.AsParallel().AsOrdered()
						.Select(im => InitMergedMessagesEntry(
							im.Message, mergedMessages[im.Index],
							displayFiltersThreadLocal, highlightFiltersThreadLocal, 
							displayFiltersPreprocessingResultCacheIsValid,
							highlightFiltersPreprocessingResultCacheIsValid,
							showRawMessages)))
					{
						MessageBase loadedMessage = preprocessedMessage.LoadedMsg;
						IThread messageThread = loadedMessage.Thread;

						bool excludedBecauseOfInvisibleThread = !messageThread.ThreadMessagesAreVisible;
						var threadsBulkProcessingResult = threadsBulkProcessing.ProcessMessage(loadedMessage);
						bool collapsed = threadsBulkProcessingResult.ThreadWasInCollapsedRegion;

						FilterAction filterAction = displayFilters.ProcessNextMessageAndGetItsAction(loadedMessage,
							preprocessedMessage.DisplayFiltersPreprocessingResult,
							threadsBulkProcessingResult.DisplayFilterContext, showRawMessages);
						bool excludedAsFilteredOut = filterAction == FilterAction.Exclude;

						loadedMessage.SetHidden(collapsed, excludedBecauseOfInvisibleThread, excludedAsFilteredOut);

						bool isHighlighted = false;
						if (!loadedMessage.IsHiddenAsFilteredOut)
						{
							FilterAction hlFilterAction = hlFilters.ProcessNextMessageAndGetItsAction(loadedMessage,
								preprocessedMessage.HighlightFiltersPreprocessingResult,
								threadsBulkProcessingResult.HighlightFilterContext, showRawMessages);
							isHighlighted = hlFilterAction == FilterAction.Include;
						}
						loadedMessage.SetHighlighted(isHighlighted);

						HandleBookmarks(bmk, loadedMessage);

						mergedMessages[loadedCount] = preprocessedMessage;
						++loadedCount;

						if (loadedMessage.IsVisible)
						{
							focusedMessageFinder.HandleNewDisplayMessage(loadedMessage, displayMessages.Count);
							AddDisplayMessage(loadedMessage);
						}
					}
				}

				System.Diagnostics.Debug.Assert(loadedCount == modelMessagesCount);

				threadsBulkProcessing.HandleHangingFrames(loadedMessagesCollection);

				tracer.Info("Update finished: displayed count={0}, loaded message={1}", displayMessages.Count, mergedMessages.Count);

				if (displayFilters != null)
					displayFilters.EndBulkProcessing(displayFiltersProcessingHandle);
				if (hlFilters != null)
					hlFilters.EndBulkProcessing(hlFiltersProcessingHandle);

				displayFiltersPreprocessingResultCacheIsValid = true;
				highlightFiltersPreprocessingResultCacheIsValid = true;
				view.UpdateScrollSizeToMatchVisibleCount();
				focusedMessageFinder.SetFoundSelection();
				if (!DisplayHintIfMessagesIsEmpty())
					DisplayEverythingFilteredOutMessageIfNeeded();
				view.Invalidate();
			}
		}

		private void ResizeMergedMessages(int modelMessagesCount)
		{
			if (mergedMessages.Capacity < modelMessagesCount)
				mergedMessages.Capacity = modelMessagesCount;
			int missingElementsCount = modelMessagesCount - mergedMessages.Count;
			if (missingElementsCount > 0)
				mergedMessages.AddRange(Enumerable.Repeat(new MergedMessagesEntry(), missingElementsCount));
			else if (missingElementsCount < 0)
				mergedMessages.RemoveRange(modelMessagesCount, -missingElementsCount);
		}

		private void UpdateDisplayMessagesCapacity(int modelMessagesCount)
		{
			displayMessages.Clear();
			displayMessages.Capacity = modelMessagesCount;
		}

		private bool DisplayHintIfMessagesIsEmpty()
		{
			if (loadedMessagesCollection.Count == 0)
			{
				string msg = model.MessageToDisplayWhenMessagesCollectionIsEmpty;
				if (!string.IsNullOrEmpty(msg))
				{
					view.DisplayNothingLoadedMessage(msg);
					return true;
				}
			}
			view.DisplayNothingLoadedMessage(null);
			return false;
		}

		private void DisplayEverythingFilteredOutMessageIfNeeded()
		{
			bool everythingFilteredOut =
				displayMessagesCollection.Count == 0
			 && loadedMessagesCollection.Count > 0
			 && model.DisplayFilters != null;
			view.DisplayEverythingFilteredOutMessage(everythingFilteredOut);
		}

		private void AddDisplayMessage(MessageBase m)
		{
			int linesCount = GetTextToDisplay(m).GetLinesCount();
			for (int i = 0; i < linesCount; ++i)
				displayMessages.Add(new DisplayMessagesEntry() { DisplayMsg = m, TextLineIndex = i });
		}

		private IBookmarksHandler CreateBookmarksHandler()
		{
			return model.Bookmarks != null ? model.Bookmarks.CreateHandler() : null;
		}

		private static FiltersList.BulkProcessingHandle BeginBulkProcessing(IFiltersList filters)
		{
			if (filters != null)
			{
				filters.PurgeDisposedFiltersAndFiltersHavingDisposedThreads();
				return filters.BeginBulkProcessing();
			}
			return null;
		}

		private static void HandleBookmarks(IBookmarksHandler bmk, MessageBase m)
		{
			if (bmk != null)
			{
				m.SetBookmarked(bmk.ProcessNextMessageAndCheckIfItIsBookmarked(m));
			}
			else
			{
				m.SetBookmarked(false);
			}
		}

		IEnumerable<IndexedMessage> MakeSearchScope(bool wrapAround, bool reverseSearch, int startFromMessage)
		{
			if (wrapAround)
			{
				if (reverseSearch)
				{
					using (Shifter shifter = loadedMessagesCollection.CreateShifter(new ShiftPermissions(true, false)))
					{
						int? firstMessageHash = new int?();
						foreach (var m in loadedMessagesCollection.Reverse(startFromMessage, int.MinValue, shifter))
						{
							if (!firstMessageHash.HasValue)
								firstMessageHash = m.Message.GetHashCode();
							yield return m;
						}
						shifter.ShiftToEnd();
						foreach (var m in loadedMessagesCollection.Reverse(int.MaxValue, startFromMessage, shifter))
						{
							if (firstMessageHash.GetValueOrDefault(0) == m.Message.GetHashCode())
								break;
							yield return m;
						}
					}
				}
				else
				{
					using (Shifter shifter = loadedMessagesCollection.CreateShifter(new ShiftPermissions(false, true)))
					{
						int? firstMessageHash = new int?();
						foreach (var m in loadedMessagesCollection.Forward(startFromMessage, int.MaxValue, shifter))
						{
							if (!firstMessageHash.HasValue)
								firstMessageHash = m.Message.GetHashCode();
							yield return m;
						}
						shifter.ShiftHome();
						foreach (var m in loadedMessagesCollection.Forward(int.MinValue, int.MaxValue, shifter))
						{
							if (firstMessageHash.GetValueOrDefault(0) == m.Message.GetHashCode())
								break;
							yield return m;
						}
					}
				}
			}
			else
			{
				var scope = reverseSearch ?
					loadedMessagesCollection.Reverse(startFromMessage, int.MinValue, new ShiftPermissions(true, false)) :
					loadedMessagesCollection.Forward(startFromMessage, int.MaxValue, new ShiftPermissions(false, true));
				foreach (var m in scope)
					yield return m;
			}
		}

		void EnsureViewUpdated()
		{
			if (model.GetAndResetPendingUpdateFlag())
			{
				InternalUpdate();
			}
		}

		IEnumerable<Tuple<int, int>> SearchResultInplaceHightlightHandler(MessageBase msg)
		{
			if (searchResultModel == null)
				yield break;
			var opts = searchResultModel.SearchParams;
			if (opts == null)
				yield break;
			int currentSearchOptionsHash = opts.GetHashCode() ^ showRawMessages.GetHashCode();
			if (lastSearchOptionsHash != currentSearchOptionsHash)
			{
				lastSearchOptionsHash = currentSearchOptionsHash;
				var tmp = opts.Options;
				tmp.SearchInRawText = showRawMessages;
				lastSearchOptionPreprocessed = tmp.Preprocess();
				inplaceHightlightHandlerState = new Search.BulkSearchState();
			}
			foreach (var r in FindAllHightlighRanges(msg, lastSearchOptionPreprocessed, inplaceHightlightHandlerState, opts.Options.ReverseSearch))
				yield return r;
		}

		static IEnumerable<Tuple<int, int>> FindAllHightlighRanges(
			MessageBase msg, 
			Search.PreprocessedOptions searchOpts, 
			Search.BulkSearchState searchState,
			bool reverseSearch)
		{
			for (int? startPos = null; ; )
			{
				var matchedTextRangle = LogJoint.Search.SearchInMessageText(msg, searchOpts, searchState, startPos);
				if (!matchedTextRangle.HasValue)
					yield break;
				if (matchedTextRangle.Value.WholeTextMatched)
					yield break;
				yield return new Tuple<int, int>(matchedTextRangle.Value.MatchBegin, matchedTextRangle.Value.MatchEnd);
				if (!reverseSearch)
					startPos = matchedTextRangle.Value.MatchEnd;
				else
					startPos = matchedTextRangle.Value.MatchBegin;
			}
		}

		IEnumerable<MessageBase> INextBookmarkCallback.EnumMessages(MessageTimestamp time, bool forward)
		{
			if (forward)
			{
				foreach (IndexedMessage l in loadedMessagesCollection.Forward(
					GetDateLowerBound(time), int.MaxValue,
						new ShiftPermissions(false, true)))
				{
					if (l.Message.Time > time)
						break;
					yield return l.Message;
				}
			}
			else
			{
				foreach (IndexedMessage l in loadedMessagesCollection.Reverse(
					GetDateUpperBound(time) - 1, int.MinValue,
						new ShiftPermissions(true, false)))
				{
					if (l.Message.Time < time)
						break;
					yield return l.Message;
				}
			}
		}

		bool SelectWordBoundaries(CursorPosition pos)
		{
			var dmsg = displayMessages[pos.DisplayIndex];
			var msg = dmsg.DisplayMsg;
			var line = GetTextToDisplay(msg).GetNthTextLine(dmsg.TextLineIndex);
			Func<KeyValuePair<int, char>, bool> isNotAWordChar = c => !StringUtils.IsWordChar(c.Value);
			int begin = line.ZipWithIndex().Take(pos.LineCharIndex).Reverse().Union(new KeyValuePair<int, char>(-1, ' ')).FirstOrDefault(isNotAWordChar).Key + 1;
			int end = line.ZipWithIndex().Skip(pos.LineCharIndex).Union(new KeyValuePair<int, char>(line.Length, ' ')).FirstOrDefault(isNotAWordChar).Key;
			if (begin != end)
			{
				SetSelection(pos.DisplayIndex, SelectionFlag.NoHScrollToSelection, begin);
				SetSelection(pos.DisplayIndex, SelectionFlag.PreserveSelectionEnd, end);
				return true;
			}
			return false;
		}

		int GetDateLowerBound(MessageTimestamp d)
		{
			return ListUtils.BinarySearch(mergedMessages, 0, mergedMessages.Count, entry => entry.LoadedMsg.Time < d);
		}

		int GetDateUpperBound(MessageTimestamp d)
		{
			return ListUtils.BinarySearch(mergedMessages, 0, mergedMessages.Count, entry => entry.LoadedMsg.Time <= d);
		}

		void GetDateEqualRange(MessageTimestamp d, out int begin, out int end)
		{
			begin = GetDateLowerBound(d);
			end = ListUtils.BinarySearch(mergedMessages, begin, mergedMessages.Count, entry => entry.LoadedMsg.Time <= d);
		}

		int? LoadedIndex2DisplayIndex(int position)
		{
			MessageBase l = this.mergedMessages[position].LoadedMsg;
			int lower = ListUtils.BinarySearch(displayMessages, 0, displayMessages.Count, dm => dm.DisplayMsg.Time < l.Time);
			int upper = ListUtils.BinarySearch(displayMessages, 0, displayMessages.Count, dm => dm.DisplayMsg.Time <= l.Time);
			for (int i = lower; i < upper; ++i)
				if (displayMessages[i].DisplayMsg == l)
					return i;
			return null;
		}

		private void SelectFoundMessageHelper(IndexedMessage? foundMessage)
		{
			bool found = foundMessage.HasValue;
			bool shownOk = false;
			if (found)
				shownOk = SelectOnlyByLoadedMessageIndex(foundMessage.Value.Index);
		}

		void UpdateSelectionInplaceHighlightingFields()
		{
			Func<MessageBase, IEnumerable<Tuple<int, int>>> newHandler = null;

			if (selection.IsSingleLine)
			{
				var normSelection = selection.Normalize();
				var line = GetTextToDisplay(normSelection.First.Message).GetNthTextLine(normSelection.First.TextLineIndex);
				int beginIdx = normSelection.First.LineCharIndex;
				int endIdx = normSelection.Last.LineCharIndex;
				if (beginIdx != endIdx && line.IsWordBoundary(beginIdx, endIdx))
				{
					var selectedPart = line.SubString(beginIdx, endIdx - beginIdx);
					if (selectedPart.All(StringUtils.IsWordChar))
					{
						var options = new LogJoint.Search.Options() 
						{
							Template = selectedPart,
							WholeWord = true,
							SearchInRawText = showRawMessages
						};
						var optionsPreprocessed = options.Preprocess();
						newHandler = msg =>
							FindAllHightlighRanges(msg, optionsPreprocessed, inplaceHightlightHandlerState, options.ReverseSearch);
					}
				}
			}

			if ((selectionInplaceHighlightingHandler != null) != (newHandler != null))
				view.Invalidate();
			else if (newHandler != null)
				view.Invalidate();

			selectionInplaceHighlightingHandler = newHandler;
		}

		readonly IModel model;
		readonly ISearchResultModel searchResultModel;
		readonly IView view;
		readonly IUINavigationHandler navHandler;

		readonly LJTraceSource tracer;
		readonly List<MergedMessagesEntry> mergedMessages = new List<MergedMessagesEntry>();
		readonly List<DisplayMessagesEntry> displayMessages = new List<DisplayMessagesEntry>();
		int mergedMessagesVersion;
		LoadedMessagesCollection loadedMessagesCollection;
		DisplayMessagesCollection displayMessagesCollection;
		SelectionInfo selection;
		bool displayFiltersPreprocessingResultCacheIsValid;
		bool highlightFiltersPreprocessingResultCacheIsValid;
		string defaultFocusedMessageActionCaption;
		LogFontSize fontSize;
		bool showTime;
		bool showMilliseconds;
		bool showRawMessages;
		bool rawViewAllowed = true;
		MessageBase slaveModeFocusedMessage;
		ColoringMode coloring = ColoringMode.Threads;
		
		Func<MessageBase, IEnumerable<Tuple<int, int>>> searchResultInplaceHightlightHandler;
		int lastSearchOptionsHash;
		Search.PreprocessedOptions lastSearchOptionPreprocessed;
		Search.BulkSearchState inplaceHightlightHandlerState = new Search.BulkSearchState();

		Func<MessageBase, IEnumerable<Tuple<int, int>>> selectionInplaceHighlightingHandler;

		#endregion
	};
};