using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace LogJoint.UI
{
	public partial class TimeLineControl : Control
	{
		public TimeLineControl()
		{
			InitializeComponent();
			
			this.SetStyle(ControlStyles.ResizeRedraw, true);
			this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
			this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
		}

		public event EventHandler<TimeNavigateEventArgs> Navigate;
		public event EventHandler<EventArgs> RangeChanged;

		public event EventHandler BeginTimeRangeDrag;
		public event EventHandler EndTimeRangeDrag;

		public static void DrawDragEllipsis(Graphics g, Rectangle r)
		{
			int y = r.Top + 1;
			for (int i = r.Left; i < r.Right; i += 5)
			{
				g.FillRectangle(Brushes.White, i+1, y+1, 2, 2);
				g.FillRectangle(Brushes.DarkGray, i, y, 2, 2);
			}
		}

		static void AddRoundRect(GraphicsPath gp, Rectangle rect, int radius)
		{
			int diameter = radius * 2; 
			Size size = new Size(diameter, diameter);
			Rectangle arc = new Rectangle(rect.Location, size); 

			gp.AddArc(arc, 180, 90);

			arc.X = rect.Right-diameter;
			gp.AddArc(arc, 270, 90);

			arc.Y = rect.Bottom-diameter;
			gp.AddArc(arc, 0, 90);

			arc.X = rect.Left;
			gp.AddArc(arc, 90, 90);

			gp.CloseFigure();
		}

		static void ApplyMinDispayHeight(ref int y1, ref int y2)
		{
			int minRangeDispayHeight = 4;
			if (y2 - y1 < minRangeDispayHeight)
			{
				y1 -= minRangeDispayHeight / 2;
				y2 += minRangeDispayHeight / 2;
			}
		}


		static void DrawTimeLineRange(Graphics g, int y1, int y2, int x1, int width, Brush brush, Pen pen)
		{
			ApplyMinDispayHeight(ref y1, ref y2);

			int radius = 3;

			if (y2 - y1 < radius * 2
			 || width < radius * 2)
			{
				g.FillRectangle(brush, x1, y1, width, y2 - y1);
				g.DrawRectangle(pen, x1, y1, width, y2 - y1);
			}
			else
			{
				GraphicsPath gp = roundRectsPath;
				gp.Reset();
				AddRoundRect(gp, new Rectangle(x1, y1, width, y2 - y1), radius);
				g.SmoothingMode = SmoothingMode.AntiAlias;
				g.FillPath(brush, gp);
				g.DrawPath(pen, gp);
				g.SmoothingMode = SmoothingMode.HighSpeed;
			}
		}

		static void DrawCutLine(Graphics g, int x1, int x2, int y, Resources res)
		{
			g.DrawLine(res.CutLinePen, x1, y - 1, x2, y - 1);
			g.DrawLine(res.CutLinePen, x1 + 2, y, x2 + 1, y);
		}

		void DrawSources(Graphics g, Metrics m, DateRange drange, ITimeGaps gaps)
		{
			Rectangle r = m.TimeLine;

			r.Inflate(-StaticMetrics.SourcesHorizontalPadding, 0);

			int sourcesCount = host.SourcesCount;

			if (sourcesCount == 0)
			{
				return;
			}
			if (r.Width / sourcesCount < 5) // Too little room to fit all sources. Give up.
			{
				return;
			}

			int sourceIdx = 0;
			foreach (ITimeLineSource src in host.Sources)
			{
				// Left-coord of this source (sourceIdx)
				int srcX     = r.Left + (sourceIdx + 0) * r.Width / sourcesCount;
				// Left-coord of the next source (sourceIdx + 1)
				int nextSrcX = r.Left + (sourceIdx + 1) * r.Width / sourcesCount;

				// Right coord of the source
				int srcRight = nextSrcX - 1;
				if (sourceIdx != sourcesCount - 1)
					srcRight -= StaticMetrics.DistanceBetweenSources;

				DateRange avaTime = src.AvailableTime;
				int y1 = GetYCoordFromDate(m, drange, gaps, avaTime.Begin);
				int y2 = GetYCoordFromDate(m, drange, gaps, avaTime.End);

				DateRange loadedTime = src.LoadedTime;
				int y3 = GetYCoordFromDate(m, drange, gaps, loadedTime.Begin);
				int y4 = GetYCoordFromDate(m, drange, gaps, loadedTime.End);

				// I pass DateRange.End property to calculate bottom Y-coords of the ranges (y2, y4).
				// DateRange.End is past-the-end value, it is 'maximim-date-belonging-to-range' + 1 tick.
				// End property yelds to the Y-coord that is 1 pixel greater than the Y-coord
				// of 'maximim-date-belonging-to-range' would be. To fix the problem we need 
				// a little correcion (bottomCoordCorrection).
				// I could use DateRange.Maximum but DateRange.End handles better the case 
				// when the range is empty.
				int endCoordCorrection = -1;

				int sourceBarWidth = srcRight - srcX - StaticMetrics.SourceShadowSize.Width;
				
				Rectangle shadowOuterRect = new Rectangle(
					srcX + StaticMetrics.SourceShadowSize.Width,
					y1 + StaticMetrics.SourceShadowSize.Height,
					sourceBarWidth + 1, // +1 because DrawShadowRect works with rect bounds similarly to FillRectange: it doesn't fill Left+Width row of pixels.
					y2 - y1 + endCoordCorrection + 1
				);

				if (DrawShadowRect.IsValidRectToDrawShadow(shadowOuterRect))
				{
					res.SourcesShadow.Draw(
						g,
						shadowOuterRect,
						Border3DSide.All
					);
				}

				// Draw the source with its native color
				using (SolidBrush sb = new SolidBrush(src.Color))
				{
					DrawTimeLineRange(g, y1, y2 + endCoordCorrection, srcX, sourceBarWidth, sb, res.SourcesBorderPen);
				}

				// Draw the loaded range with a bit darker color
				using (SolidBrush sb = new SolidBrush(PastelColorsGenerator.MakeDarker(src.Color)))
				{
					DrawTimeLineRange(g, y3, y4 + endCoordCorrection, srcX, sourceBarWidth, sb, res.SourcesBorderPen);
				}


				foreach (TimeGap gap in gaps)
				{
					// Ignore irrelevant gaps
					if (!avaTime.IsInRange(gap.Range.Begin))
						continue;

					int gy1 = GetYCoordFromDate(m, drange, gaps, gap.Range.Begin);
					int gy2 = GetYCoordFromDate(m, drange, gaps, gap.Range.End);

					gy1 += StaticMetrics.MinimumTimeSpanHeight / 2;
					gy2 -= StaticMetrics.MinimumTimeSpanHeight / 2;

					g.FillRectangle(
						res.Background, 
						srcX, 
						gy1, 
						srcRight - srcX + 1, 
						gy2 - gy1 + endCoordCorrection + 1
					);

					int tempRectHeight = DrawShadowRect.MinimumRectSize.Height + 1;
					Rectangle shadowTmp = new Rectangle(
						shadowOuterRect.X,
						gy1 - tempRectHeight + StaticMetrics.SourceShadowSize.Height + 1,
						shadowOuterRect.Width,
						tempRectHeight
					);

					if (DrawShadowRect.IsValidRectToDrawShadow(shadowTmp))
					{
						res.SourcesShadow.Draw(g, shadowTmp, Border3DSide.Bottom | Border3DSide.Middle | Border3DSide.Right);
					}

					DrawCutLine(g, srcX, srcX + sourceBarWidth, gy1, res);
					DrawCutLine(g, srcX, srcX + sourceBarWidth, gy2, res);
				}



				++sourceIdx;
			}
		}

		static string GetRulerLabelFormat(RulerMark rm)
		{
			string labelFmt = null;
			switch (rm.Component)
			{
				case DateComponent.Year:
					labelFmt = "yyyy";
					break;
				case DateComponent.Month:
					if (rm.IsMajor)
						labelFmt = "Y"; // year+month
					else
						labelFmt = "MMM";
					break;
				case DateComponent.Day:
					if (rm.IsMajor)
						labelFmt = "m";
					else
						labelFmt = "dd (ddd)";
					break;
				case DateComponent.Hour:
					labelFmt = "t";
					break;
				case DateComponent.Minute:
					labelFmt = "t";
					break;
				case DateComponent.Seconds:
					labelFmt = "T";
					break;
				case DateComponent.Milliseconds:
					labelFmt = "ffff";
					break;
			}
			return labelFmt;
		}

		void DrawRulers(Graphics g, Metrics m, DateRange drange, ITimeGaps gaps, RulerIntervals? rulerIntervals)
		{
			if (!rulerIntervals.HasValue)
				return;
			g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

			foreach (RulerMark rm in GenerateRulerMarks(rulerIntervals.Value, drange, gaps))
			{
				int y = GetYCoordFromDate(m, drange, gaps, rm.Time);
				g.DrawLine(rm.IsMajor ? res.RulersPen2 : res.RulersPen1, 0, y, m.Client.Width, y);
				string labelFmt = GetRulerLabelFormat(rm);
				if (labelFmt != null)
				{
					string label = rm.Time.ToString(labelFmt);
					g.DrawString(label, res.RulersFont, Brushes.White, 3 + 1, y + 1);
					g.DrawString(label, res.RulersFont, Brushes.Gray, 3, y);
				}
			}

			g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;
		}

		void DrawDragAreas(Graphics g, Metrics m, RulerIntervals? rulerIntervals)
		{
			using (StringFormat fmt = new StringFormat())
			{
				fmt.Alignment = StringAlignment.Center;
				int center = (m.Client.Left + m.Client.Right) / 2;

				g.FillRectangle(SystemBrushes.ButtonFace, new Rectangle(
					0, m.TopDrag.Y, m.Client.Width, m.TopDate.Bottom - m.TopDrag.Y
				));
				g.DrawString(
					GetUserFriendlyFullDateTimeString(range.Begin, rulerIntervals),
					this.Font, Brushes.Black, center, m.TopDate.Top, fmt);
				DrawDragEllipsis(g, m.TopDrag);

				g.FillRectangle(SystemBrushes.ButtonFace, new Rectangle(
					0, m.BottomDate.Y, m.Client.Width, m.BottomDrag.Bottom - m.BottomDate.Y
				));
				g.DrawString(GetUserFriendlyFullDateTimeString(range.End, rulerIntervals),
					this.Font, Brushes.Black, center, m.BottomDate.Top, fmt);
				DrawDragEllipsis(g, m.BottomDrag);
			}
		}

		void DrawBookmarks(Graphics g, Metrics m, DateRange drange)
		{
			foreach (IBookmark bmk in host.Bookmarks)
			{
				if (!drange.IsInRange(bmk.Time))
					continue;
				int y = GetYCoordFromDate(m, drange, gaps, bmk.Time);
				bool hidden = false;
				if (bmk.Thread != null)
					if (!bmk.Thread.ThreadMessagesAreVisible)
						hidden = true;
				Pen bookmarkPen = res.BookmarkPen;
				if (hidden)
					bookmarkPen = res.HiddenBookmarkPen;
				g.DrawLine(bookmarkPen, m.Client.Left, y, m.Client.Right, y);
				Image img = this.bookmarkPictureBox.Image;
				g.DrawImage(img,
					m.Client.Right - img.Width - 2,
					y - 2,
					img.Width,
					img.Height
				);
			}
		}

		void DrawCurrentViewTime(Graphics g, Metrics m, DateRange drange)
		{
			DateTime? curr = host.CurrentViewTime;
			if (curr.HasValue && drange.IsInRange(curr.Value))
			{
				int y = GetYCoordFromDate(m, drange, gaps, curr.Value);
				g.DrawLine(res.CurrentViewTimePen, m.Client.Left, y, m.Client.Right, y);
			}
		}

		void DrawHotTrackDate(Graphics g, Metrics m, DateRange drange)
		{
			if (hotTrackDate.HasValue)
			{
				int y = GetYCoordFromDate(m, drange, gaps, hotTrackDate.Value);
				GraphicsState s = g.Save();
				g.SmoothingMode = SmoothingMode.AntiAlias;
				g.TranslateTransform(0, y);
				g.DrawLine(res.HotTrackLinePen, 0, 0, m.TimeLine.Right, 0);
				g.FillPath(Brushes.Red, res.HotTrackMarker);
				g.TranslateTransform(m.TimeLine.Width - 1, 0);
				g.ScaleTransform(-1, 1, MatrixOrder.Prepend);
				g.FillPath(Brushes.Red, res.HotTrackMarker);
				g.Restore(s);
			}
		}

		void DrawFocusRect(Graphics g)
		{
			if (Focused)
			{
				if (host.FocusRectIsRequired)
				{
					ControlPaint.DrawFocusRectangle(g, this.ClientRectangle);
				}
			}
		}

		protected override void OnPaint(PaintEventArgs pe)
		{
			Graphics g = pe.Graphics;

			// Fill the background
			g.FillRectangle(res.Background, ClientRectangle);

			// Display range. Equal to the animation range in there is active animation.
			DateRange drange = animationRange.GetValueOrDefault(range);

			if (drange.IsEmpty)
				return;

			if (host.SourcesCount == 0)
				return;

			Metrics m = GetMetrics();

			RulerIntervals? rulerIntervals = FindRulerIntervals(m, drange.Length.Ticks);

			DrawSources(g, m, drange, gaps);

			DrawRulers(g, m, drange, gaps, rulerIntervals);

			DrawDragAreas(g, m, rulerIntervals);

			DrawBookmarks(g, m, drange);

			DrawCurrentViewTime(g, m, drange);

			DrawHotTrackDate(g, m, drange);

			DrawFocusRect(g);

			base.OnPaint(pe);
		}

		public void SetHost(ITimeLineControlHost host)
		{
			this.host = host;
			InternalUpdate();
		}

		protected override void OnGotFocus(EventArgs e)
		{
			Invalidate();
			base.OnGotFocus(e);
		}

		protected override void OnLostFocus(EventArgs e)
		{
			Invalidate();
			base.OnLostFocus(e);
		}

		public void UpdateView()
		{
			InternalUpdate();
		}

		void UpdateRange()
		{
			DateRange union = DateRange.MakeEmpty();
			foreach (ITimeLineSource s in host.Sources)
				union = DateRange.Union(union, s.AvailableTime);
			DateRange newRange;
			if (range.IsEmpty)
			{
				newRange = union;
			}
			else
			{
				DateRange tmp = DateRange.Intersect(union, range);
				if (tmp.IsEmpty)
				{
					newRange = union;
				}
				else
				{
					newRange = new DateRange(
						range.Begin == availableRange.Begin ? union.Begin : tmp.Begin,
						range.End == availableRange.End ? union.End : tmp.End
					);
				}
			}
			DoSetRange(newRange);
			availableRange = union;
		}

		void UpdateDatesSize()
		{
			using (Graphics g = this.CreateGraphics())
			{
				SizeF tmp = g.MeasureString("0123", this.Font);
				datesSize = new Size((int)Math.Ceiling(tmp.Width),
					(int)Math.Ceiling(tmp.Height));
			}
		}

		void UpdateGaps()
		{
			gaps = host.TimeGaps;
		}

		void InternalUpdate()
		{
			StopDragging(false);

			UpdateRange();
			UpdateDatesSize();
			UpdateGaps();

			if (range.IsEmpty)
			{
				RelaseStatusReport();
				ReleaseHotTrack();
			}

			Invalidate();
		}

		public DateRange TimeRange
		{
			get { return range; }
		}

		public DateRange AvailableTimeRange
		{
			get { return availableRange; }
		}

		static double GetTimeDensity(Metrics m, DateRange range, ITimeGaps gaps)
		{
			switch (gapsDisplayMode)
			{
				case GapsDisplayMode.ExcludeFromTimeLine:
					return (double)(m.TimeLine.Height - gaps.Count * Metrics.GapHeight) / (range.Length - gaps.Length).TotalMilliseconds;
				case GapsDisplayMode.KeepTimeLineScale:
					return (double)m.TimeLine.Height / range.Length.TotalMilliseconds;
				default:
					throw new ArgumentException();
			}
		}

		static int GetYCoordFromDate(Metrics m, DateRange range, ITimeGaps gaps, DateTime t)
		{
			return GetYCoordFromDate(m, range, gaps, GetTimeDensity(m, range, gaps), t);
		}

		static double GetYCoordFromDate_UniformScale(Metrics m, DateRange range, double density, DateTime t)
		{
			TimeSpan pos = t - range.Begin;
			double ret = (double)(m.TimeLine.Top + pos.TotalMilliseconds * density);
			return ret;
		}

		static double GetYCoordFromDate_NonUniformScale(Metrics m, DateRange range, ITimeGaps gaps, double density, DateTime t)
		{
			// Find the closest gap that is located after (or covers) the date in question (t)
			int nextGapIdx = gaps.BinarySearch(0, gaps.Count, delegate(TimeGap g) { return g.Range.End <= t; });

			// Amount of gaps. It defines Y-coordinate offset connected to the fact that there are gaps before date (t).
			// These gaps take some place on the timeline: see gapsOffset * Metrics.GapHeigh in the final formula.
			int gapsOffset = nextGapIdx;

			// Check if the gap found covers the date (t)
			if (nextGapIdx != gaps.Count && gaps[nextGapIdx].Range.IsInRange(t))
			{
				// If it is we want to stick to the boundaries of the gap. 

				TimeGap next = gaps[nextGapIdx];

				if (t > next.Mid) // Add extra Metrics.GapHeight to Y-coordinate if (t) is over the second half of the range.
				{
					++gapsOffset;
				}

				t = next.Range.Begin; // Stick to the beginning of the range
			}

			// Time span that defines Y-coordinate offset that is proportional to date (t). 
			TimeSpan timeOffset = t - range.Begin;

			// If the gap found is not the first one
			if (nextGapIdx > 0)
			{
				// Get the gap that preceeds the date in question (t)
				TimeGap prev = gaps[nextGapIdx - 1];

				// Substract the time that is covered by time gaps and that we exclude from timeline
				timeOffset -= prev.CumulativeLengthInclusive;
			}

			// The final formula:
			double ret = (double)(
				m.TimeLine.Top + // The origin of Y coordinates
				gapsOffset * Metrics.GapHeight + // The space that is taken by gaps' lines (gaps are shown by fixed height lines)
				timeOffset.TotalMilliseconds * density // The actual offset that depends on (t)
			);

			return ret;
		}

		static int GetYCoordFromDate(Metrics m, DateRange range, ITimeGaps gaps, double density, DateTime t)
		{
			double ret;

			switch (gapsDisplayMode)
			{
				case GapsDisplayMode.KeepTimeLineScale:
					ret = GetYCoordFromDate_UniformScale(m, range, density, t);
					break;
				case GapsDisplayMode.ExcludeFromTimeLine:
					ret = GetYCoordFromDate_NonUniformScale(m, range, gaps, density, t);
					break;
				default:
					throw new ArgumentException();
			}

			// Limit the value to be able to fit to int
			ret = Math.Min(ret, 1e6);
			ret = Math.Max(ret, -1e6);

			return (int)ret;
		}

		DateTime GetDateFromYCoord(Metrics m, int y, out NavigateFlag navFlags)
		{
			DateTime ret = GetDateFromYCoord(m, range, gaps, y, out navFlags);
			ret = availableRange.PutInRange(ret);
			if (ret == availableRange.End && ret != DateTime.MinValue)
				ret = availableRange.Maximum;
			return ret;
		}

		DateTime GetDateFromYCoord(Metrics m, int y)
		{
			NavigateFlag navFlags;
			return GetDateFromYCoord(m, y, out navFlags);
		}

		static DateTime GetDateFromYCoord_UniformScale(Metrics m, DateRange range, int y)
		{
			double percent = (double)(y - m.TimeLine.Top) / (double)m.TimeLine.Height;

			TimeSpan toAdd = TimeSpan.FromMilliseconds(percent * range.Length.TotalMilliseconds);

			try
			{
				return range.Begin + toAdd;
			}
			catch (ArgumentOutOfRangeException)
			{
				// There might be overflow
				if (toAdd.Ticks <= 0)
					return range.Begin;
				else
					return range.End;
			}
		}

		static DateTime GetDateFromYCoord_NonUniformScale(Metrics m, DateRange range, ITimeGaps gaps, int y, out NavigateFlag navFlag)
		{
			navFlag = NavigateFlag.AlignCenter | NavigateFlag.OriginDate;

			double density = GetTimeDensity(m, range, gaps);

			// Get the closest gap that is after the Y coordinate in quetstion (y)
			int nextGap = gaps.BinarySearch(0, gaps.Count, delegate(TimeGap g)
			{
				return GetYCoordFromDate(m, range, gaps, density, g.Range.Begin) <= y;
			});

			// A date that would be an origin of the uniform range nearest to coordinate (y).
			// 'Uniform range' means that we can use *density or /density operations to 
			// convert between dates and coordinates.
			DateTime origin;

			// An offset inside the uniform range
			int dy;

			if (nextGap == 0) // If the gap found is the first one
			{
				// then the origin of the uniform range is the very beginning of the timeline

				origin = range.Begin;
				dy = y - m.TimeLine.Top;
			}
			else
			{
				// otherwise the origin is the end of the previos gap

				DateRange prev = gaps[nextGap - 1].Range; // Get the dates range of the prev. gap
				int tmp = y - GetYCoordFromDate(m, range, gaps, density, prev.End); // Get the offset in a temp variable
				if (tmp < 0 && tmp >= -Metrics.GapHeight) // If the offset is inside the area that a gap fills
				{
					// then stick to gap boundaries
					dy = 0;
					if (tmp >= -Metrics.GapHeight / 2)
					{
						origin = prev.End;
						navFlag = NavigateFlag.AlignBottom | NavigateFlag.OriginDate;
					}
					else
					{
						origin = prev.Begin;
						navFlag = NavigateFlag.AlignTop | NavigateFlag.OriginDate;
					}
				}
				else
				{
					// otherwise use gap's end as an origin
					dy = tmp;
					origin = prev.End;
				}
			}

			// Get the time span we need to add to the origin
			TimeSpan toAdd = TimeSpan.FromMilliseconds((double)dy / density);

			try
			{
				return origin + toAdd;
			}
			catch (ArgumentOutOfRangeException)
			{
				// There might be overflow
				if (toAdd.Ticks <= 0)
					return range.Begin;
				else
					return range.End;
			}
		}

		static DateTime GetDateFromYCoord(Metrics m, DateRange range, ITimeGaps gaps, int y)
		{
			NavigateFlag navFlags;
			return GetDateFromYCoord(m, range, gaps, y, out navFlags);
		}

		static DateTime GetDateFromYCoord(Metrics m, DateRange range, ITimeGaps gaps, int y, out NavigateFlag navFlags)
		{
			switch (gapsDisplayMode)
			{
				case GapsDisplayMode.ExcludeFromTimeLine:
					return GetDateFromYCoord_NonUniformScale(m, range, gaps, y, out navFlags);
				case GapsDisplayMode.KeepTimeLineScale:
					navFlags = NavigateFlag.AlignCenter | NavigateFlag.OriginDate;
					return GetDateFromYCoord_UniformScale(m, range, y);
				default:
					throw new ArgumentException();
			}
		}

		public enum DragArea
		{
			Top, Bottom
		};

		protected override void OnMouseDown(MouseEventArgs e)
		{
			this.Focus();

			if (range.IsEmpty)
				return;

			Metrics m = GetMetrics();

			if (e.Button == MouseButtons.Left)
			{
				if (m.TimeLine.Contains(e.Location))
				{
					NavigateFlag navFlags;
					DateTime d = GetDateFromYCoord(m, e.Y, out navFlags);
					FireNavigateEvent(d, navFlags);
				}
				else if (m.TopDate.Contains(e.Location))
				{
					FireNavigateEvent(range.Begin, 
						availableRange.Begin == range.Begin ?
						(NavigateFlag.AlignTop | NavigateFlag.OriginStreamBoundaries) :
						(NavigateFlag.AlignCenter | NavigateFlag.OriginDate));
				}
				else if (m.BottomDate.Contains(e.Location))
				{
					FireNavigateEvent(range.End,
						availableRange.End == range.End ? 
						(NavigateFlag.AlignBottom | NavigateFlag.OriginStreamBoundaries) :
						(NavigateFlag.AlignCenter | NavigateFlag.OriginDate));
				}
				else if (m.TopDrag.Contains(e.Location) || m.BottomDrag.Contains(e.Location))
				{
					dragPoint = e.Location;
					OnBeginTimeRangeDrag();
				}
			}
			else if (e.Button == MouseButtons.Middle)
			{
				if (m.TimeLine.Contains(e.Location))
				{
					dragPoint = e.Location;
					dragRange = range;
					OnBeginTimeRangeDrag();
				}
			}
		}

		protected override void OnDoubleClick(EventArgs e)
		{
			Point screenPt = Control.MousePosition;
			Point pt = this.PointToClient(screenPt);
			Metrics m = GetMetrics();
			if (m.TopDrag.Contains(pt))
			{
				if (range.Begin != availableRange.Begin)
				{
					StopDragging(false);
					DoSetRangeAnimated(new DateRange(availableRange.Begin, range.End));
					Invalidate();
				}
			}
			else if (m.BottomDrag.Contains(pt))
			{
				if (range.End != availableRange.End)
				{
					StopDragging(false);
					DoSetRangeAnimated(new DateRange(range.Begin, availableRange.End));
					Invalidate();
				}
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			if (!this.Capture)
				StopDragging(false);

			if (range.IsEmpty)
			{
				this.Cursor = Cursors.Default;
				return;
			}

			Metrics m = GetMetrics();
			if (dragPoint.HasValue)
			{
				if (m.TimeLine.Contains(dragPoint.Value))
				{
					DoSetRange(dragRange);
					ShiftRange(GetDateFromYCoord(m, dragRange, gaps, dragPoint.Value.Y) - GetDateFromYCoord(m, dragRange, gaps, e.Y));
				}
				else
				{
					Point mousePt = this.PointToScreen(new Point(dragPoint.Value.X, e.Y));

					if (dragForm == null)
						dragForm = new TimeLineDragForm(this);

					DragArea area = m.TopDrag.Contains(dragPoint.Value) ? DragArea.Top : DragArea.Bottom;
					dragForm.Area = area;

					DateTime d = GetDateFromYCoord(
						m,
						e.Y - dragPoint.Value.Y +
							(area == DragArea.Top ? m.TimeLine.Top : m.TimeLine.Bottom)
					);
					d = availableRange.PutInRange(d);
					dragForm.Date = d;

					Point pt1 = this.PointToScreen(new Point());
					Point pt2 = this.PointToScreen(new Point(ClientSize.Width, 0));
					int formHeight = datesSize.Height + DragAreaHeight;
					dragForm.SetBounds(
						pt1.X,
						pt1.Y + (int)GetYCoordFromDate(m, range, gaps, d) +
							(area == DragArea.Top ? -formHeight : 0),
						pt2.X - pt1.X,
						formHeight
					);

					GetStatusReport().SetStatusString(
						string.Format("Changing the {0} bound of the time line to {1}. ESC to cancel.",
						area == DragArea.Top ? "upper" : "lower",
							GetUserFriendlyFullDateTimeString(d)));

					if (!dragForm.Visible)
					{
						dragForm.Visible = true;
						this.Focus();
					}
				}
			}
			else
			{
				if (m.TopDrag.Contains(e.Location) || m.BottomDrag.Contains(e.Location))
				{
					this.Cursor = Cursors.SizeNS;
					if (hotTrackDate.HasValue)
					{
						hotTrackDate = new DateTime?();
						Invalidate();
					}

					string txt;
					if (m.TopDrag.Contains(e.Location))
					{
						txt = "Click and drag to change the lower bound of the time line.";
						if (range.Begin != availableRange.Begin)
							txt += " Double-click to restore the initial value.";
					}
					else
					{
						txt = "Click and drag to change the upper bound of the time line.";
						if (range.End != availableRange.End)
							txt += " Double-click to restore the initial value.";
					}

					GetStatusReport().SetStatusString(txt);
				}
				else
				{
					this.Cursor = Cursors.Default;
					UpdateHotTrackDate(m, e.Y);
					Invalidate();
				}
			}
		}

		void UpdateHotTrackDate(Metrics m, int y)
		{
			hotTrackDate = range.PutInRange(GetDateFromYCoord(m, y));
			string msg = "";
			if (range.End == availableRange.End && m.BottomDate.Contains(m.BottomDate.Left, y))
				msg = string.Format("Click to stick to the end of the log");
			else
				msg = string.Format("Click to see what was happening at around {0}.{1}",
						GetUserFriendlyFullDateTimeString(hotTrackDate.Value),
						Focused ? " Ctrl + Mouse Wheel to zoom view." : "");
			GetStatusReport().SetStatusString(msg);
		}

		protected virtual void OnBeginTimeRangeDrag()
		{
			if (BeginTimeRangeDrag != null)
				BeginTimeRangeDrag(this, EventArgs.Empty);
		}

		protected virtual void OnEndTimeRangeDrag()
		{
			if (EndTimeRangeDrag != null)
				EndTimeRangeDrag(this, EventArgs.Empty);
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			if (range.IsEmpty) // todo: create IsEmpty method to check if the control is empty
				return;

			Metrics m = GetMetrics();
			if (Control.ModifierKeys == Keys.Control)
			{
				ZoomRange(GetDateFromYCoord(m, e.Location.Y), -e.Delta);
			}
			else
			{
				ShiftRange(-e.Delta);
				UpdateHotTrackDate(m, e.Location.Y);
			}
		}

		void ZoomRange(DateTime zoomAt, int delta)
		{
			if (delta == 0)
				return;

			long scale = 100 + Math.Sign(delta) * 20;

			DateRange tmp = new DateRange(
				zoomAt - new TimeSpan((zoomAt - range.Begin).Ticks * scale / 100),
				zoomAt + new TimeSpan((range.End - zoomAt).Ticks * scale / 100)
			);

			DoSetRange(DateRange.Intersect(availableRange, tmp));

			Invalidate();
		}

		void ShiftRange(int delta)
		{
			if (delta == 0)
				return;

			ShiftRange(new TimeSpan(Math.Sign(delta) * range.Length.Ticks / 20));
		}

		void ShiftRange(TimeSpan offset)
		{
			long offsetTicks = offset.Ticks;

			offsetTicks = Math.Max(offsetTicks, (availableRange.Begin - range.Begin).Ticks);
			offsetTicks = Math.Min(offsetTicks, (availableRange.End - range.End).Ticks);

			offset = new TimeSpan(offsetTicks);

			DoSetRange(new DateRange(range.Begin + offset, range.End + offset));

			Invalidate();
		}


		void DoSetRangeAnimated(DateRange newRange)
		{
			if (newRange.IsEmpty || newRange.Begin == newRange.Maximum)
				return;
			if (range.Equals(newRange))
				return;

			int stepsCount = 8;
			TimeSpan deltaB = new TimeSpan((newRange.Begin - range.Begin).Ticks / stepsCount);
			TimeSpan deltaE = new TimeSpan((newRange.End - range.End).Ticks / stepsCount);
			bool animateDragForm = dragForm != null && dragForm.Visible;
			Metrics m = GetMetrics();

			DateRange i = range;
			for (int step = 0; step < stepsCount; ++step)
			{
				animationRange = i;
				if (animateDragForm)
				{
					if (deltaB.Ticks != 0)
					{
						int y = GetYCoordFromDate(m, animationRange.Value, gaps, newRange.Begin);
						dragForm.Top = this.PointToScreen(new Point(0, y)).Y - dragForm.Height;
					}
					else if (deltaE.Ticks != 0)
					{
						int y = GetYCoordFromDate(m, animationRange.Value, gaps, newRange.End);
						dragForm.Top = this.PointToScreen(new Point(0, y)).Y;
					}
				}

				this.Refresh();
				System.Threading.Thread.Sleep(20);

				i = new DateRange(i.Begin + deltaB, i.End + deltaE);
			}
			animationRange = new DateRange?();

			DoSetRange(newRange);
		}


		void StopDragging(bool accept)
		{
			if (dragPoint.HasValue)
			{
				if (accept && dragForm != null && dragForm.Visible)
				{
					if (dragForm.Area == DragArea.Top)
					{
						DoSetRangeAnimated(new DateRange(dragForm.Date, range.End));
					}
					else
					{
						DoSetRangeAnimated(new DateRange(range.Begin, dragForm.Date));
					}
					Invalidate();
				}
				dragPoint = new Point?();
				OnEndTimeRangeDrag();
			}
			if (dragForm != null && dragForm.Visible)
			{
				dragForm.Visible = false;
			}
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			StopDragging(true);
			base.OnMouseUp(e);
		}

		protected override void OnMouseCaptureChanged(EventArgs e)
		{
			StopDragging(false);
			base.OnMouseCaptureChanged(e);
		}

		void FireNavigateEvent(DateTime val, NavigateFlag flags)
		{
			DateTime newVal = range.PutInRange(val);
			if (newVal == range.End)
				newVal = range.Maximum;
			if (Navigate != null)
				Navigate(this, new TimeNavigateEventArgs(newVal, flags));
		}

		protected override bool IsInputKey(Keys keyData)
		{
			if (keyData == Keys.Escape)
				return false;
			return base.IsInputKey(keyData);
		}

		protected override bool ProcessDialogKey(Keys keyData)
		{
			return base.ProcessDialogKey(keyData);
		}

		protected override bool ProcessCmdKey(ref System.Windows.Forms.Message msg, Keys keyData)
		{
			if (keyData == Keys.Escape)
			{
				if (dragPoint.HasValue)
				{
					StopDragging(false);
				}
			}
			return base.ProcessCmdKey(ref msg, keyData);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Apps)
			{
			}
			base.OnKeyDown(e);
		}

		void RelaseStatusReport()
		{
			if (statusReport != null)
			{
				statusReport.Dispose();
				statusReport = null;
			}
		}

		void ReleaseHotTrack()
		{
			if (hotTrackDate.HasValue)
			{
				hotTrackDate = new DateTime?();
				Invalidate();
			}
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			RelaseStatusReport();
			ReleaseHotTrack();
			base.OnMouseLeave(e);
		}

		struct Metrics
		{
			public Rectangle Client;
			public Rectangle TopDrag;
			public Rectangle TopDate;
			public Rectangle TimeLine;
			public Rectangle BottomDate;
			public Rectangle BottomDrag;

			public const int GapHeight = 5;
		};

		Metrics GetMetrics()
		{
			Metrics r;
			r.Client = this.ClientRectangle;
			r.TopDrag = new Rectangle(DragAreaHeight / 2, 0, r.Client.Width - DragAreaHeight, DragAreaHeight);
			r.TopDate = new Rectangle(0, r.TopDrag.Bottom, r.Client.Width, datesSize.Height);
			r.BottomDrag = new Rectangle(DragAreaHeight / 2, r.Client.Height - DragAreaHeight, r.Client.Width - DragAreaHeight, DragAreaHeight);
			r.BottomDate = new Rectangle(0, r.BottomDrag.Top - datesSize.Height, r.Client.Width, datesSize.Height);
			r.TimeLine = new Rectangle(0, r.TopDate.Bottom, r.Client.Width, 
				r.BottomDate.Top - r.TopDate.Bottom - StaticMetrics.SourceShadowSize.Height - StaticMetrics.SourcesBottomPadding);
			return r;
		}

		IStatusReport GetStatusReport()
		{
			if (statusReport == null)
				statusReport = host.GetStatusReport();
			return statusReport;
		}

		class Resources: IDisposable
		{
			public readonly Brush Background = SystemBrushes.Window;
			public readonly DrawShadowRect SourcesShadow = new DrawShadowRect(Color.Gray);
			public readonly Pen SourcesBorderPen = Pens.DimGray;
			public readonly Pen CutLinePen;
			public readonly Pen RulersPen1, RulersPen2;
			public readonly Font RulersFont;
			public readonly Pen BookmarkPen;
			public readonly Pen HiddenBookmarkPen;
			public readonly Pen CurrentViewTimePen = Pens.Blue;
			public readonly GraphicsPath HotTrackMarker;
			public readonly Pen HotTrackLinePen;

			public Resources()
			{
				CutLinePen = new Pen(SourcesBorderPen.Color);
				CutLinePen.DashPattern = new float[] { 2, 2 };

				RulersPen1 = new Pen(Color.Gray, 1);
				RulersPen1.DashPattern = new float[] { 1, 3 };
				RulersPen2 = new Pen(Color.Gray, 1);
				RulersPen2.DashPattern = new float[] { 4, 1 };
				RulersFont = new Font("Tahoma", 6);

				BookmarkPen = new Pen(Color.FromArgb(0x5b, 0x87, 0xe0));
				HiddenBookmarkPen = (Pen)BookmarkPen.Clone();
				HiddenBookmarkPen.DashPattern = new float[] { 10, 3 };

				HotTrackMarker = new GraphicsPath();
				HotTrackMarker.AddPolygon(new Point[] {
					new Point(4, 0),
					new Point(0, 4),
					new Point(0, -4)
				});
				HotTrackLinePen = new Pen(Color.FromArgb(128, Color.Red), 1);

			}

			public void Dispose()
			{
				SourcesShadow.Dispose();
				CutLinePen.Dispose();
				RulersPen1.Dispose();
				RulersPen2.Dispose();
				RulersFont.Dispose();
				BookmarkPen.Dispose();
				HiddenBookmarkPen.Dispose();
				HotTrackMarker.Dispose();
				HotTrackLinePen.Dispose();
			}
		};

		static class StaticMetrics
		{
			/// <summary>
			/// distace between the borders of the control and the bars showing sources
			/// </summary>
			public const int SourcesHorizontalPadding = 1;
			/// <summary>
			/// distace between the bottom border of the control and the bars showing sources
			/// </summary>
			public const int SourcesBottomPadding = 1;
			/// <summary>
			/// distance between sources' bars (when there are more than one source)
			/// </summary>
			public const int DistanceBetweenSources = 1;
			/// <summary>
			/// px. Size of the shadow that log source bars drop.
			/// </summary>
			public static readonly Size SourceShadowSize = new Size(2, 2);
			/// <summary>
			/// The height of the line that is drawn to show the gaps in messages (see DrawCutLine())
			/// </summary>
			public const int CutLineHeight = 2;
			/// <summary>
			/// Minimum height (px) that a time span may have. Time span is a range between time gaps.
			/// We have to limit the miminum size because of usability problems. User must be able to
			/// see and click on any time span even if it very small.
			/// </summary>
			public const int MinimumTimeSpanHeight = 4;
		};

		enum DateComponent
		{
			None,
			Year,
			Month,
			Day,
			Hour,
			Minute,
			Seconds,
			Milliseconds
		};

		struct RulerInterval
		{
			public readonly TimeSpan Duration;
			public readonly DateComponent Component;
			public readonly int NonUniformComponentCount;
			public bool IsHiddenWhenMajor;

			public RulerInterval(TimeSpan dur, int nonUniformComonentCount, DateComponent comp)
			{
				Duration = dur;
				Component = comp;
				NonUniformComponentCount = nonUniformComonentCount;
				IsHiddenWhenMajor = false;
			}

			public RulerInterval MakeHiddenWhenMajor()
			{
				IsHiddenWhenMajor = true;
				return this;
			}

			public static RulerInterval FromYears(int years)
			{
				return new RulerInterval(DateTime.MinValue.AddYears(years) - DateTime.MinValue, years, DateComponent.Year);
			}
			public static RulerInterval FromMonths(int months)
			{
				return new RulerInterval(DateTime.MinValue.AddMonths(months) - DateTime.MinValue, months, DateComponent.Month);
			}
			public static RulerInterval FromDays(int days)
			{
				return new RulerInterval(TimeSpan.FromDays(days), 0, DateComponent.Day);
			}
			public static RulerInterval FromHours(int hours)
			{
				return new RulerInterval(TimeSpan.FromHours(hours), 0, DateComponent.Hour);
			}
			public static RulerInterval FromMinutes(int minutes)
			{
				return new RulerInterval(TimeSpan.FromMinutes(minutes), 0, DateComponent.Minute);
			}
			public static RulerInterval FromSeconds(double seconds)
			{
				return new RulerInterval(TimeSpan.FromSeconds(seconds), 0, DateComponent.Seconds);
			}
			public static RulerInterval FromMilliseconds(double mseconds)
			{
				return new RulerInterval(TimeSpan.FromMilliseconds(mseconds), 0, DateComponent.Milliseconds);
			}

			public DateTime StickToIntervalBounds(DateTime d)
			{
				if (Component == DateComponent.Year)
				{
					int year = (d.Year / NonUniformComponentCount) * NonUniformComponentCount;
					if (year == 0)
						return d;
					return new DateTime(year, 1, 1);
				}

				if (Component == DateComponent.Month)
					return new DateTime(d.Year, ((d.Month - 1) / NonUniformComponentCount) * NonUniformComponentCount + 1, 1);

				long durTicks = Duration.Ticks;

				if (durTicks == 0)
					return d;

				return new DateTime((d.Ticks / Duration.Ticks) * Duration.Ticks);
			}

			public DateTime MoveDate(DateTime d)
			{
				if (Component == DateComponent.Year)
					return d.AddYears(NonUniformComponentCount);
				if (Component == DateComponent.Month)
					return d.AddMonths(NonUniformComponentCount);
				return d.Add(Duration);
			}

		};

		static readonly RulerInterval[] predefinedRulerIntervals = new RulerInterval[]
		{
			RulerInterval.FromYears(1000),
			RulerInterval.FromYears(100),
			RulerInterval.FromYears(25),
			RulerInterval.FromYears(5),
			RulerInterval.FromYears(1),
			RulerInterval.FromMonths(3),
			RulerInterval.FromMonths(1).MakeHiddenWhenMajor(),
			RulerInterval.FromDays(7).MakeHiddenWhenMajor(),
			RulerInterval.FromDays(3),
			RulerInterval.FromDays(1),
			RulerInterval.FromHours(6),
			RulerInterval.FromHours(1),
			RulerInterval.FromMinutes(20),
			RulerInterval.FromMinutes(5),
			RulerInterval.FromMinutes(1),
			RulerInterval.FromSeconds(20),
			RulerInterval.FromSeconds(5),
			RulerInterval.FromSeconds(1),
			RulerInterval.FromMilliseconds(200),
			RulerInterval.FromMilliseconds(50),
			RulerInterval.FromMilliseconds(10),
			RulerInterval.FromMilliseconds(2),
			RulerInterval.FromMilliseconds(1)
		};

		struct RulerIntervals
		{
			public readonly RulerInterval Major, Minor;
			public RulerIntervals(RulerInterval major, RulerInterval minor)
			{
				Major = major;
				Minor = minor;
			}
		};

		struct RulerMark
		{
			public readonly DateTime Time;
			public readonly bool IsMajor;
			public readonly DateComponent Component;

			public RulerMark(DateTime d, bool isMajor, DateComponent comp)
			{
				Time = d;
				IsMajor = isMajor;
				Component = comp;
			}
		};

		RulerIntervals? FindRulerIntervals()
		{
			return FindRulerIntervals(GetMetrics(), range.Length.Ticks);
		}

		static long MulDiv(long a, int b, int c)
		{
			long whole = (a / c) * b;
			long fraction = (a % c) * b / c;
			return whole + fraction;
		}

		RulerIntervals? FindRulerIntervals(Metrics m, long totalTicks)
		{
			if (totalTicks <= 0)
				return null;
			int minMarkHeight = 25;
			return FindRulerIntervals(
				new TimeSpan(MulDiv(totalTicks, minMarkHeight, m.TimeLine.Height)));
		}

		static RulerIntervals? FindRulerIntervals(TimeSpan minSpan)
		{
			for (int i = predefinedRulerIntervals.Length - 1; i >= 0; --i)
			{
				if (predefinedRulerIntervals[i].Duration > minSpan)
				{
					if (i == 0)
						i = 1;
					return new RulerIntervals(predefinedRulerIntervals[i - 1], predefinedRulerIntervals[i]);
				}
			}
			return null;
		}

		struct SkipTimeGapsHelper
		{
			ITimeGaps gaps;
			int count;
			int idx;
			DateRange current;

			public SkipTimeGapsHelper(ITimeGaps gaps)
			{
				this.gaps = gaps;
				this.count = gaps.Count;
				this.idx = 0;
				this.current = idx < count ? gaps[idx].Range : new DateRange();
			}

			public bool AdjustDate(ref DateTime d)
			{
				while (idx < count && d >= current.End)
				{
					++idx;
					if (idx < count)
						current = gaps[idx].Range;
				}
				if (idx < count && d >= current.Begin)
				{
					d = current.End;
					return true;
				}
				return false;
			}
		};

		static IEnumerable<RulerMark> GenerateRulerMarks(RulerIntervals intervals, DateRange range, ITimeGaps gaps)
		{
			RulerInterval major = intervals.Major;
			RulerInterval minor = intervals.Minor;

			DateTime lastMajor = DateTime.MaxValue;
			for (DateTime d = major.StickToIntervalBounds(range.Begin);
				d < range.End; d = minor.MoveDate(d))
			{
				if (d < range.Begin)
					continue;
				if (!major.IsHiddenWhenMajor)
				{
					DateTime tmp = major.StickToIntervalBounds(d);
					if (tmp >= range.Begin && tmp != lastMajor)
					{
						yield return new RulerMark(tmp, true, major.Component);
						lastMajor = tmp;
						if (tmp == d)
							continue;
					}
					yield return new RulerMark(d, false, minor.Component);
				}
				else
				{
					yield return new RulerMark(d, true, minor.Component);
				}
			}
		}

		public bool AreMillisecondsVisible
		{
			get			
			{
				return AreMillisecondsVisibleInternal(FindRulerIntervals());
			}
		}

		public string GetUserFriendlyFullDateTimeString(DateTime d)
		{
			return GetUserFriendlyFullDateTimeString(d, FindRulerIntervals());
		}

		string GetUserFriendlyFullDateTimeString(DateTime d, RulerIntervals? ri)
		{
			return MessageBase.FormatTime(d, AreMillisecondsVisibleInternal(ri));
		}

		static bool AreMillisecondsVisibleInternal(RulerIntervals? ri)
		{
			return ri.HasValue && ri.Value.Minor.Component == DateComponent.Milliseconds;
		}
		
		bool DoSetRange(DateRange r)
		{
			if (r.Equals(range))
				return false;
			range = r;
			if (RangeChanged != null)
				RangeChanged(this, EventArgs.Empty);
			return true;
		}

		private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
		{
			if (range.IsEmpty)
			{
				e.Cancel = true;
				return;
			}
			
			resetTimeLineMenuItem.Visible = !availableRange.Equals(range);
			viewTailModeMenuItem.Checked = host.IsInViewTailMode;
		}

		private void contextMenuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
		{
			if (e.ClickedItem == resetTimeLineMenuItem)
			{
				DoSetRangeAnimated(availableRange);
			}
			else if (e.ClickedItem == viewTailModeMenuItem)
			{
				FireNavigateEvent(availableRange.End, 
					!viewTailModeMenuItem.Checked ?
					(NavigateFlag.AlignBottom | NavigateFlag.OriginStreamBoundaries) :
					(NavigateFlag.AlignCenter | NavigateFlag.OriginDate));
			}
		}

		public const int DragAreaHeight = 5;

		ITimeLineControlHost host;
		DateRange availableRange;
		DateRange range;
		DateRange? animationRange;
		ITimeGaps gaps;
		Size datesSize;
		IStatusReport statusReport;
		DateTime? hotTrackDate;
		Point? dragPoint;
		DateRange dragRange;
		TimeLineDragForm dragForm;
		static readonly GraphicsPath roundRectsPath = new GraphicsPath();
		enum GapsDisplayMode
		{
			ExcludeFromTimeLine,
			KeepTimeLineScale
		};
		static readonly GapsDisplayMode gapsDisplayMode = GapsDisplayMode.KeepTimeLineScale;
		Resources res = new Resources();
	}

	class DrawShadowRect : IDisposable
	{
		readonly Color color;
		SolidBrush inner, border1, border2, edge1, edge2, edge3;

		SolidBrush CreateHalftone(int alpha)
		{
			return new SolidBrush(Color.FromArgb(alpha, color));
		}

		/// <summary>
		/// The minimum size of a rectangle that can be rendered by Draw()
		/// </summary>
		public static readonly Size MinimumRectSize = new Size(4, 4);

		public DrawShadowRect(Color cl)
		{
			color = cl;
			inner = CreateHalftone(255);
			border1 = CreateHalftone(191);
			border2 = CreateHalftone(63);
			edge1 = CreateHalftone(143);
			edge2 = CreateHalftone(47);
			edge3 = CreateHalftone(15);
		}
		public void Dispose()
		{
			inner.Dispose();
			border1.Dispose();
			border2.Dispose();
			edge1.Dispose();
			edge2.Dispose();
			edge3.Dispose();
		}

		public static bool IsValidRectToDrawShadow(Rectangle r)
		{
			return r.Width >= MinimumRectSize.Width && r.Height >= MinimumRectSize.Height;
		}

		public void Draw(Graphics g, Rectangle r, Border3DSide sides)
		{
			if (!IsValidRectToDrawShadow(r))
			{
				throw new ArgumentException("Rect is too small", "r");
			}
			 
			r.Inflate(-2, -2);

			if ((sides & Border3DSide.Middle) != 0)
			{
				g.FillRectangle(inner, r);
			}

			if ((sides & Border3DSide.Top) != 0)
			{
				g.FillRectangle(border1, r.Left, r.Top - 1, r.Width, 1);
				g.FillRectangle(border2, r.Left, r.Top - 2, r.Width, 1);
			}
			if ((sides & Border3DSide.Right) != 0)
			{
				g.FillRectangle(border1, r.Right, r.Top, 1, r.Height);
				g.FillRectangle(border2, r.Right + 1, r.Top, 1, r.Height);
			}
			if ((sides & Border3DSide.Bottom) != 0)
			{
				g.FillRectangle(border1, r.Left, r.Bottom, r.Width, 1);
				g.FillRectangle(border2, r.Left, r.Bottom + 1, r.Width, 1);
			}
			if ((sides & Border3DSide.Left) != 0)
			{
				g.FillRectangle(border1, r.Left - 1, r.Top, 1, r.Height);
				g.FillRectangle(border2, r.Left - 2, r.Top, 1, r.Height);
			}

			if ((sides & Border3DSide.Left) != 0 && (sides & Border3DSide.Top) != 0)
			{
				g.FillRectangle(edge1, r.Left - 1, r.Top - 1, 1, 1);
				g.FillRectangle(edge2, r.Left - 2, r.Top - 1, 1, 1);
				g.FillRectangle(edge2, r.Left - 1, r.Top - 2, 1, 1);
				g.FillRectangle(edge3, r.Left - 2, r.Top - 2, 1, 1);
			}

			if ((sides & Border3DSide.Top) != 0 && (sides & Border3DSide.Right) != 0)
			{
				g.FillRectangle(edge1, r.Right, r.Top - 1, 1, 1);
				g.FillRectangle(edge2, r.Right, r.Top - 2, 1, 1);
				g.FillRectangle(edge2, r.Right + 1, r.Top - 1, 1, 1);
				g.FillRectangle(edge3, r.Right + 1, r.Top - 2, 1, 1);
			}

			if ((sides & Border3DSide.Right) != 0 && (sides & Border3DSide.Bottom) != 0)
			{
				g.FillRectangle(edge1, r.Right, r.Bottom, 1, 1);
				g.FillRectangle(edge2, r.Right + 1, r.Bottom, 1, 1);
				g.FillRectangle(edge2, r.Right, r.Bottom + 1, 1, 1);
				g.FillRectangle(edge3, r.Right + 1, r.Bottom + 1, 1, 1);
			}

			if ((sides & Border3DSide.Bottom) != 0 && (sides & Border3DSide.Left) != 0)
			{
				g.FillRectangle(edge1, r.Left - 1, r.Bottom, 1, 1);
				g.FillRectangle(edge2, r.Left - 1, r.Bottom + 1, 1, 1);
				g.FillRectangle(edge2, r.Left - 2, r.Bottom, 1, 1);
				g.FillRectangle(edge3, r.Left - 2, r.Bottom + 1, 1, 1);
			}
		}
	};

	public interface ITimeLineSource
	{
		DateRange AvailableTime { get; }
		DateRange LoadedTime { get; }
		Color Color { get; }
	};

	public interface ITimeLineControlHost
	{
		IEnumerable<ITimeLineSource> Sources { get; }
		int SourcesCount { get; }
		DateTime? CurrentViewTime { get; }
		IStatusReport GetStatusReport();
		IEnumerable<IBookmark> Bookmarks { get; }
		bool FocusRectIsRequired { get; }
		bool IsInViewTailMode { get; }
		ITimeGaps TimeGaps { get; }
	};

	public class TimeNavigateEventArgs : EventArgs
	{
		public TimeNavigateEventArgs(DateTime date, NavigateFlag flags)
		{
			this.date = date;
			this.flags = flags;
		}
		public DateTime Date { get { return date; } }
		public NavigateFlag Flags { get { return flags; } }

		DateTime date;
		NavigateFlag flags;
	}
}