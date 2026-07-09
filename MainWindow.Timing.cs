using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace BpmMeasurer;

/// <summary>
/// Timing-point CRUD (raw points ↔ computed timing points), the sidebar segment-list
/// rebuild, and the add/remove-segment button handlers. Extracted from MainWindow as a
/// partial. Segment row UI is built via <see cref="SegmentRowFactory"/>.
/// </summary>
public partial class MainWindow
{
    // ── Timing refresh ──

    private void RefreshTimingPoints()
    {
        _timingPoints = TimingEngine.RecalculateTiming(_globalOffset, _rawPoints);
        OffsetStepper.SetValue(_globalOffset);
        RebuildSegmentList();

        if (_audioData != null && (_plotsConfigured || _specConfigured))
        {
            RenderBeatGrid();
            RenderBeatRow();
        }
    }

    // ── Raw points editing helpers ──

    private void UpdateRawBpm(Guid id, double bpm)
    {
        if (_isPlaying) PausePlayback();
        bpm = Math.Clamp(Math.Round(bpm * 1000.0) / 1000.0, 10, 1000);
        for (int i = 0; i < _rawPoints.Count; i++)
        {
            if (_rawPoints[i].Id == id)
            {
                _rawPoints[i] = new RawTimingPoint(id, _rawPoints[i].BeatIndex, bpm, _rawPoints[i].MaxBeatIndex, _rawPoints[i].BeatsPerBar);
                break;
            }
        }
        RefreshTimingPoints();
    }

    private void UpdateRawBeatsPerBar(Guid id, double beatsPerBar)
    {
        if (_isPlaying) PausePlayback();
        int beats = (int)Math.Clamp(Math.Round(beatsPerBar), 1, 20);
        for (int i = 0; i < _rawPoints.Count; i++)
        {
            if (_rawPoints[i].Id == id)
            {
                _rawPoints[i] = new RawTimingPoint(id, _rawPoints[i].BeatIndex, _rawPoints[i].Bpm, _rawPoints[i].MaxBeatIndex, beats);
                break;
            }
        }
        RefreshTimingPoints();
    }

    private void UpdateRawBeatIndex(Guid id, double beatIndex)
    {
        if (_isPlaying) PausePlayback();
        beatIndex = Math.Max(0.001, beatIndex);
        beatIndex = Math.Round(beatIndex * 1000.0) / 1000.0;

        // Clamp to the frozen cap captured when this segment was created (does not change afterwards).
        int segIdx = -1;
        for (int i = 0; i < _rawPoints.Count; i++)
            if (_rawPoints[i].Id == id) { segIdx = i; break; }
        if (segIdx > 0)
        {
            double frozenMax = _rawPoints[segIdx].MaxBeatIndex;
            if (frozenMax < double.MaxValue)
                beatIndex = Math.Min(beatIndex, frozenMax);
        }

        if (_rawPoints.Any(p => p.Id != id && Math.Abs(p.BeatIndex - beatIndex) < 0.001))
            return; // duplicate beat index — reject silently
        for (int i = 0; i < _rawPoints.Count; i++)
        {
            if (_rawPoints[i].Id == id)
            {
                _rawPoints[i] = new RawTimingPoint(id, beatIndex, _rawPoints[i].Bpm, _rawPoints[i].MaxBeatIndex, _rawPoints[i].BeatsPerBar);
                break;
            }
        }
        _rawPoints.Sort((a, b) => a.BeatIndex.CompareTo(b.BeatIndex));
        RefreshTimingPoints();
    }

    private void RemoveRawPoint(Guid id)
    {
        var target = _rawPoints.FirstOrDefault(p => p.Id == id);
        if (target.BeatIndex == 0) return; // anchor (beat 0) is never removable
        _rawPoints.RemoveAll(p => p.Id == id);
        RefreshTimingPoints();
    }

    /// <summary>
    /// Max beat index the segment at <paramref name="segIdx"/> may start at, so that its
    /// start time does not exceed the audio duration and it stays before the next segment.
    /// </summary>
    private double GetMaxBeatIndexForSegment(int segIdx)
    {
        // segIdx == _timingPoints.Count means a hypothetical new segment appended after the last.
        if (_audioData == null || segIdx <= 0 || segIdx > _timingPoints.Count)
            return double.MaxValue;

        var prev = _timingPoints[segIdx - 1];
        double duration = _audioData.Duration;

        // Segment time = prev.Time + (beat - prev.BeatIndex) * 60 / prev.Bpm  <=  duration
        double timeBasedMax = prev.BeatIndex + (duration - prev.Time) * prev.Bpm / 60.0;
        double max = timeBasedMax;

        if (segIdx + 1 < _timingPoints.Count)
        {
            double nextBeat = _timingPoints[segIdx + 1].BeatIndex;
            max = Math.Min(max, nextBeat - 0.001);
        }

        // Always keep at least a 1-beat gap from the previous segment.
        return Math.Max(prev.BeatIndex + 1.0, max);
    }

    // ── Segment list panel (sidebar) ──

    private void RebuildSegmentList()
    {
        // Save / restore the scroll offset so editing a value (typing, BPM drag) never shifts
        // the viewport — even at the very bottom where WPF would otherwise re-anchor to the
        // new content height. Auto-expand paths set _scrollExpandedToBottom to instead pin the
        // expanded segment's bottom edge to the viewport bottom.
        double savedOffset = SegmentScrollViewer.VerticalOffset;
        bool scrollToBottom = _scrollExpandedToBottom;
        _scrollExpandedToBottom = false;

        SegmentListPanel.Children.Clear();

        for (int i = 0; i < _timingPoints.Count; i++)
        {
            var point = _timingPoints[i];
            bool isAnchor = Math.Abs(point.BeatIndex) < 0.001;
            bool isExpanded = _expandedSegmentId.HasValue && _expandedSegmentId.Value == point.Id;

            Border row = isExpanded
                ? BuildExpandedSegmentRow(i, point, isAnchor)
                : BuildCollapsedSegmentRow(i, point, isAnchor);

            SegmentListPanel.Children.Add(row);
        }

        if (scrollToBottom)
            ScrollExpandedToBottom();
        else
            SegmentScrollViewer.ScrollToVerticalOffset(savedOffset);
    }

    /// <summary>
    /// Scrolls the ScrollViewer so the currently-expanded segment's bottom edge sits at the
    /// bottom of the viewport. Forces a layout pass first so ActualHeight / transforms reflect
    /// the just-rebuilt tree.
    /// </summary>
    private void ScrollExpandedToBottom()
    {
        if (!_expandedSegmentId.HasValue) return;

        int idx = -1;
        for (int i = 0; i < _timingPoints.Count; i++)
        {
            if (_timingPoints[i].Id == _expandedSegmentId.Value) { idx = i; break; }
        }
        if (idx < 0 || idx >= SegmentListPanel.Children.Count) return;

        // Force a synchronous layout pass so positions are current after the rebuild.
        SegmentScrollViewer.UpdateLayout();

        if (!(SegmentListPanel.Children[idx] is FrameworkElement row)) return;

        // Bottom edge of the row relative to the StackPanel (scroll content root).
        var origin = row.TransformToVisual(SegmentListPanel).Transform(new Point(0, 0));
        double rowBottom = origin.Y + row.ActualHeight + row.Margin.Bottom;
        double desired = rowBottom - SegmentScrollViewer.ViewportHeight;
        double clamped = Math.Max(0, Math.Min(desired, SegmentScrollViewer.ScrollableHeight));
        SegmentScrollViewer.ScrollToVerticalOffset(clamped);
    }

    /// <summary>
    /// Outer row shell: the Border (background, accent left-bar, padding). Carries the
    /// segment id in its Tag and a single bubbling MouseLeftButtonDown handler, so clicking
    /// anywhere on the row toggles expand/collapse — except on interactive children
    /// (steppers / text boxes / the ✕ button), which handle their own mouse input and are
    /// additionally filtered by <see cref="IsInsideInteractive"/> as a safety net.
    /// </summary>
    private Border CreateSegmentRowBorder(TimingPoint point, bool isAnchor)
    {
        var accent = Color.FromRgb(0x81, 0x8C, 0xF8);

        // A segment is illegal if its start time has been pushed past the audio end
        // (e.g. by a later global-offset increase). Warn with a red background.
        bool isIllegal = !isAnchor && _audioData != null && point.Time > _audioData.Duration;
        Color rowBg = isIllegal
            ? Color.FromRgb(0x3A, 0x1E, 0x1E)
            : Color.FromRgb(0x1A, 0x1A, 0x1A);

        var row = new Border
        {
            Background = new SolidColorBrush(rowBg),
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 6, 6),
            Margin = new Thickness(0, 0, 0, 8),
            Tag = point.Id,
            Cursor = Cursors.Hand
        };
        row.MouseLeftButtonDown += Row_ToggleExpand;
        return row;
    }

    private TextBlock MakeHeaderLabel(int index) => new()
    {
        Text = string.Format(Loc("Segment_Label"), index),
        Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>
    /// Collapsed-row header: just the segment index as a number (e.g. "1"), dropping the
    /// localized "段落" prefix to save horizontal space on the dense two-line row.
    /// </summary>
    private TextBlock MakeCollapsedHeaderNumber(int index) => new()
    {
        Text = index.ToString(),
        Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>
    /// The always-visible ✕ remove button. The glyph is a bright, slightly white-ish red;
    /// hovering fills a square rounded background gray. Uses a custom ControlTemplate (a
    /// plain Border bound to the button's Background) instead of the default theme chrome,
    /// whose own MouseOver highlight (a bright blue) would otherwise show through regardless
    /// of the Background value. Callers place it in the rightmost Auto column so its x-position
    /// is identical in collapsed and expanded states.
    /// </summary>
    private static Button MakeRemoveButton(Guid id)
    {
        var btn = new Button
        {
            Content = "✕",
            Tag = id,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x85, 0x85)),
            Cursor = Cursors.Hand,
            FontSize = 11,
            Width = 20,
            Height = 20
        };
        btn.Template = MakeRemoveButtonTemplate();
        btn.Style = new Style(typeof(Button))
        {
            Setters =
            {
                new Setter(Control.BackgroundProperty, Brushes.Transparent)
            },
            Triggers =
            {
                new Trigger
                {
                    Property = UIElement.IsMouseOverProperty,
                    Value = true,
                    Setters =
                    {
                        new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)))
                    }
                }
            }
        };
        return btn;
    }

    private static ControlTemplate MakeRemoveButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    /// <summary>Expanded row: full stepper layout (the pre-existing design).</summary>
    private Border BuildExpandedSegmentRow(int index, TimingPoint point, bool isAnchor)
    {
        var row = CreateSegmentRowBorder(point, isAnchor);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = MakeHeaderLabel(index);
        Grid.SetRow(header, 0);
        Grid.SetColumn(header, 0);
        grid.Children.Add(header);

        if (!isAnchor)
        {
            var removeBtn = MakeRemoveButton(point.Id);
            removeBtn.Click += RemoveSegmentBtn_Click;
            Grid.SetRow(removeBtn, 0);
            Grid.SetColumn(removeBtn, 1);
            grid.Children.Add(removeBtn);
        }

        // Beat + BPM inputs (2 rows × 2 cols)
        //   Row 0: offset(beat) | beats-per-bar
        //   Row 1: BPM spanning both columns
        var inputsGrid = new Grid();
        inputsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inputsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        inputsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetRow(inputsGrid, 2);
        Grid.SetColumnSpan(inputsGrid, 2);
        grid.Children.Add(inputsGrid);

        FrameworkElement beatField;
        if (isAnchor)
        {
            beatField = SegmentRowFactory.BuildStaticField(
                Loc("Beat_Label"), "0", Color.FromRgb(0x99, 0x99, 0x99), 0, 0);
        }
        else
        {
            beatField = SegmentRowFactory.BuildStepper(
                Loc("Beat_Label"),
                new[] { 1.0, 0.1 }, 0.001, point.MaxBeatIndex, 3,
                Color.FromRgb(0xDD, 0xDD, 0xDD),
                point.Id, false, point.BeatIndex, 0,
                v =>
                {
                    UpdateRawBeatIndex(point.Id, v);
                    RecordTimingIfChanged();
                }, 0);
        }
        inputsGrid.Children.Add(beatField);

        var bpbPanel = SegmentRowFactory.BuildStepper(
            Loc("BeatsPerBar_Label"),
            new[] { 1.0 }, 1, 20, 0,
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            point.Id, false, point.BeatsPerBar, 1,
            v =>
            {
                UpdateRawBeatsPerBar(point.Id, v);
                RecordTimingIfChanged();
            }, 0);
        inputsGrid.Children.Add(bpbPanel);

        var bpmPanel = SegmentRowFactory.BuildStepper(
            Loc("Bpm_Label"),
            new[] { 10.0, 1.0, 0.1 }, 10, 1000, 3,
            Color.FromRgb(0x00, 0xF2, 0xFF),
            point.Id, false, point.Bpm, 0,
            v =>
            {
                UpdateRawBpm(point.Id, v);
                RecordTimingIfChanged();
            }, 2);
        Grid.SetColumnSpan(bpmPanel, 2);
        inputsGrid.Children.Add(bpmPanel);

        // Start time footer (red + warning when the segment is illegal)
        bool isIllegal = !isAnchor && _audioData != null && point.Time > _audioData.Duration;
        var time = new TextBlock
        {
            Text = isIllegal
                ? $"{Loc("StartTime_Label")}  {point.Time:F3}s  ⚠ {Loc("Segment_Illegal")}"
                : $"{Loc("StartTime_Label")}  {point.Time:F3}s",
            Foreground = new SolidColorBrush(isIllegal
                ? Color.FromRgb(0xEF, 0x44, 0x44)
                : Color.FromRgb(0x81, 0x8C, 0xF8)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            FontWeight = FontWeights.Bold
        };
        Grid.SetRow(time, 4);
        Grid.SetColumnSpan(time, 2);
        grid.Children.Add(time);

        row.Child = grid;
        return row;
    }

    /// <summary>
    /// Collapsed (two-line) segment row. Every value uses the same colours as the expanded
    /// steppers. Line 1 = header | beat | beats-per-bar | ✕; line 2 = BPM | start time.
    /// Labels and values are placed in shared grid columns so the line-1 and line-2 values
    /// stack vertically aligned (beat under BPM, beats-per-bar under start time).
    /// The two value columns are Star-sized so they split remaining space and clip naturally;
    /// ✕ is fixed-width and always at the right edge.
    /// </summary>
    private Border BuildCollapsedSegmentRow(int index, TimingPoint point, bool isAnchor)
    {
        var row = CreateSegmentRowBorder(point, isAnchor);
        bool isIllegal = !isAnchor && _audioData != null && point.Time > _audioData.Duration;

        var grid = new Grid();
        // Pinned layout: header + labels are Auto-sized; the two value columns split all
        // remaining space equally (Star). ✕ has a fixed width so it never shifts or
        // overflows. Long values are clipped naturally by the constrained column width.
        //   header | label1 | value1 ★ | label2 | value2 ★ | ✕(fixed)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // C0 header
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // C1 label1
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // C2 value1
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // C3 label2
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // C4 value2
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });                   // C5 ✕ (fixed)
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Colours mirror the expanded steppers: beat gray / bpb white / bpm cyan / time indigo.
        Color beatColor = isAnchor ? Color.FromRgb(0x99, 0x99, 0x99) : Color.FromRgb(0xDD, 0xDD, 0xDD);
        Color bpbColor = Color.FromRgb(0xFF, 0xFF, 0xFF);
        Color bpmColor = Color.FromRgb(0x00, 0xF2, 0xFF);
        Color timeColor = isIllegal ? Color.FromRgb(0xEF, 0x44, 0x44) : Color.FromRgb(0x81, 0x8C, 0xF8);
        Color labelColor = Color.FromRgb(0x66, 0x66, 0x66);

        string beatText = Math.Abs(point.BeatIndex % 1.0) < 0.0001 ? ((long)Math.Round(point.BeatIndex)).ToString() : point.BeatIndex.ToString("0.###");
        string bpmText = point.Bpm.ToString("0.000");
        string bpbText = point.BeatsPerBar.ToString();
        string timeText = $"{point.Time:F3}s" + (isIllegal ? $"  ⚠ {Loc("Segment_Illegal")}" : "");

        // Line 1: header | beat | beats-per-bar | ✕
        Place(grid, MakeCollapsedHeaderNumber(index), 0, 0);
        Place(grid, MakeCell(Loc("Beat_Label"), labelColor, 12), 0, 1);
        Place(grid, MakeCell(beatText, beatColor, 2), 0, 2);
        Place(grid, MakeCell(Loc("BeatsPerBar_Label"), labelColor, 14), 0, 3);
        Place(grid, MakeCell(bpbText, bpbColor, 2), 0, 4);

        if (!isAnchor)
        {
            var removeBtn = MakeRemoveButton(point.Id);
            removeBtn.Click += RemoveSegmentBtn_Click;
            Place(grid, removeBtn, 0, 5);
        }

        // Line 2: BPM | start time (header and ✕ columns left empty so the label/value
        // columns line up exactly under line 1).
        Place(grid, MakeCell(Loc("Bpm_Label"), labelColor, 12), 2, 1);
        Place(grid, MakeCell(bpmText, bpmColor, 2), 2, 2);
        Place(grid, MakeCell(Loc("StartTime_Label"), labelColor, 14), 2, 3);
        Place(grid, MakeCell(timeText, timeColor, 2), 2, 4);

        row.Child = grid;
        return row;
    }

    private static TextBlock MakeCell(string text, Color color, double marginLeft = 0) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(color),
        FontFamily = new FontFamily("Consolas"),
        FontSize = 11,
        FontWeight = FontWeights.Bold,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(marginLeft, 0, 0, 0)
    };

    private static void Place(Grid grid, FrameworkElement el, int row, int col)
    {
        Grid.SetRow(el, row);
        Grid.SetColumn(el, col);
        grid.Children.Add(el);
    }

    /// <summary>
    /// Bubbling handler on each row Border: toggle expand/collapse unless the press started
    /// on an interactive child. Buttons (stepper ±, ✕) mark their own MouseLeftButtonDown
    /// handled so they never reach here; the TextBox / StepperInput guard covers the cases
    /// where a child did not mark the event handled.
    /// </summary>
    private void Row_ToggleExpand(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideInteractive(e.OriginalSource as DependencyObject))
            return;
        if (sender is FrameworkElement fe && fe.Tag is Guid id)
            ExpandSegment(id);
    }

    private static bool IsInsideInteractive(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is TextBox || element is Controls.StepperInput)
                return true;
            element = element is Visual
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    /// <summary>
    /// Toggles the expanded segment: clicking a collapsed row expands it (and collapses
    /// every other, since only one may be open); clicking the header of the currently-open
    /// row collapses it (back to all-collapsed). Mutating the raw points (UpdateRaw*) never
    /// calls this — only the header / info-line click handlers do.
    /// </summary>
    private void ExpandSegment(Guid id)
    {
        _expandedSegmentId = (_expandedSegmentId.HasValue && _expandedSegmentId.Value == id)
            ? null
            : id;
        RebuildSegmentList();
    }

    /// <summary>
    /// Resets the expanded row to the beat-0 anchor segment. Called after audio load and
    /// config import (full data replaces) so the list opens on a predictable segment.
    /// </summary>
    private void ResetExpandedSegmentToAnchor()
    {
        _expandedSegmentId = _rawPoints.Count > 0 ? _rawPoints[0].Id : null;
    }

    private void AddSegmentBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_audioData == null || _timingPoints.Count == 0) return;

        // Snap to the nearest beat at the playhead position.
        double t = Math.Clamp(_viewCenterTime, 0, _audioData.Duration);
        double beatF = TimingEngine.GetBeatIndexAtTime(t, _timingPoints);
        double newBeat = Math.Max(1.0, Math.Round(beatF));

        // Rule: only append after the last existing beat index.
        double maxBeat = _rawPoints.Max(p => p.BeatIndex);
        if (newBeat <= maxBeat)
            newBeat = maxBeat + 1.0;

        // Constraint: the new (last) segment's start time must not exceed audio duration.
        double durationMax = GetMaxBeatIndexForSegment(_timingPoints.Count); // hypothetical appended segment
        if (newBeat > durationMax)
            return; // no valid slot before the audio end — abort silently

        // Default BPM and BeatsPerBar inherit from the last (previous) segment.
        // Freeze the beat-index cap at creation time; it will not change afterwards even if
        // offset/duration shift makes this segment's time invalid (UI turns red instead).
        double prevBpm = _rawPoints[^1].Bpm;
        int prevBeatsPerBar = _rawPoints[^1].BeatsPerBar;
        var newId = Guid.NewGuid();
        _rawPoints.Add(new RawTimingPoint(newId, newBeat, prevBpm, durationMax, prevBeatsPerBar));
        _rawPoints.Sort((a, b) => a.BeatIndex.CompareTo(b.BeatIndex));
        _expandedSegmentId = newId; // auto-expand the freshly added segment
        _scrollExpandedToBottom = true;
        RefreshTimingPoints();
        RecordTimingIfChanged();
    }

    private void RemoveSegmentBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid id)
        {
            RemoveRawPoint(id);
            RecordTimingIfChanged();
        }
    }
}
