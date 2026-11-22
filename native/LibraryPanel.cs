using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Mini = MusicWidget.NativeMini;

namespace MusicWidget.NativeApp
{
    internal sealed partial class LibraryPanel : Grid
    {
        private readonly WidgetWindow window;
        private readonly HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Grid view;
        private ListBox list;
        private Grid rowsHost;
        private DockPanel content;
        private FrameworkElement placeholder;
        private Canvas tabIndicatorLayer;
        private Border tabIndicator;
        private TranslateTransform tabShift;
        private readonly Dictionary<string, Button> playlistTabs = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private StackPanel playlistTools;
        private Button editPlaylist, deletePlaylist;
        private TextBlock selectedCount;
        private Button addSelected;
        private TextBox search;
        private string libraryCount;
        private string librarySearch = "";
        private string pickerPlaylist;
        private string route = "library";

        internal static LibraryPanel Build(WidgetWindow w)
        {
            return new LibraryPanel(w);
        }

        private LibraryPanel(WidgetWindow w)
        {
            window = w;
            Loaded += delegate { window.Model.LibraryChanged += OnLibraryChanged; };
            Unloaded += delegate { window.Model.LibraryChanged -= OnLibraryChanged; CancelDrag(); };
            ShowLibrary(false);
        }

        private void SwitchView(FrameworkElement header, bool back, bool animated)
        {
            CancelDrag();
            Children.Clear();
            list = null;
            placeholder = null;
            view = new Grid();
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition());
            view.Children.Add(header);
            content = new DockPanel { LastChildFill = true };
            Grid.SetRow(content, 1);
            view.Children.Add(content);
            Children.Add(view);
            if (animated) PanelKit.Enter(view, window.Model.Settings.ReducedMotion, back);
        }

        private void Dock(UIElement element, Dock position)
        {
            DockPanel.SetDock(element, position);
            content.Children.Add(element);
        }

        private void ShowLibrary(bool back)
        {
            route = "library";
            window.SetLibraryEditor(false);
            pickerPlaylist = null;
            selected.Clear();
            var tracks = window.Model.TracksFor(window.Model.ActivePlaylist);
            libraryCount = TrackCount(tracks.Count);
            SwitchView(MainHeader(), back, back);

            var actions = new Grid { Margin = new Thickness(0, 0, 0, 15) };
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            var add = window.Glass.Button(window.T("addMusic"), window.OpenAddMusic, "plus", 11, 13);
            var create = window.Glass.Button(window.T("newPlaylist"), () => ShowPlaylistEditor(null), labelSize: 11);
            add.Padding = create.Padding = new Thickness(6, 0, 6, 0);
            add.Height = create.Height = 36;
            actions.Children.Add(add);
            Grid.SetColumn(create, 2);
            actions.Children.Add(create);
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            playlistTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            editPlaylist = window.Glass.IconButton("edit", "", () => ShowPlaylistEditor(window.Model.ActivePlaylist), 32);
            deletePlaylist = window.Glass.IconButton("trash", "", RemovePlaylist, 32);
            deletePlaylist.Margin = new Thickness(5, 0, 0, 0);
            playlistTools.Children.Add(editPlaylist);
            playlistTools.Children.Add(deletePlaylist);
            Grid.SetColumn(playlistTools, 3);
            actions.Children.Add(playlistTools);
            UpdatePlaylistAction();
            Dock(actions, System.Windows.Controls.Dock.Top);

            var tabs = BuildTabs();
            tabs.Margin = new Thickness(0, 0, 0, 14);
            Dock(tabs, System.Windows.Controls.Dock.Top);
            Dock(BuildSearch(librarySearch), System.Windows.Controls.Dock.Top);
            BuildTrackArea();
            RefreshRows();
        }

        private FrameworkElement BuildTabs()
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3) };
            var stack = new Grid();
            tabIndicatorLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            tabShift = new TranslateTransform();
            tabIndicator = window.Glass.SelectionIndicator();
            tabIndicator.RenderTransform = tabShift;
            tabIndicator.Height = 34;
            Canvas.SetTop(tabIndicator, 3);
            tabIndicatorLayer.Children.Add(tabIndicator);
            stack.Children.Add(tabIndicatorLayer);
            stack.Children.Add(buttons);
            playlistTabs.Clear();
            foreach (string name in new[] { "ALL" }.Concat(window.Model.Playlists.Select(item => item.Name)))
            {
                var button = window.Glass.Chip(SameName(name, "ALL") ? window.T("all") : name, false,
                    delegate { OpenPlaylist(name); });
                button.Height = 34;
                button.MaxWidth = 160;
                buttons.Children.Add(button);
                playlistTabs.Add(name, button);
            }
            stack.Loaded += delegate { MoveTabIndicator(false); };
            buttons.SizeChanged += delegate { MoveTabIndicator(false); };
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                PanningMode = PanningMode.HorizontalOnly, Focusable = false, Content = stack
            };
            return PanelKit.Track(scroll);
        }

        private void MoveTabIndicator(bool animate)
        {
            Button active;
            if (!playlistTabs.TryGetValue(window.Model.ActivePlaylist, out active) || active.ActualWidth <= 0) return;
            double x = active.TranslatePoint(new Point(), tabIndicatorLayer).X;
            double oldX = tabShift.X;
            double oldWidth = tabIndicator.Width;
            tabShift.X = x;
            tabIndicator.Width = active.ActualWidth;
            bool slide = animate && window.Motion && oldWidth > 0;
            tabShift.BeginAnimation(TranslateTransform.XProperty, slide ? Mini.Glass.Transition(oldX, x, 520, Mini.Glass.Spring) : null);
            tabIndicator.BeginAnimation(WidthProperty, slide ? Mini.Glass.Transition(oldWidth, active.ActualWidth, 520, Mini.Glass.Spring) : null);
        }

        private void UpdatePlaylistAction()
        {
            playlistTools.Visibility = IsAll ? Visibility.Collapsed : Visibility.Visible;
            if (IsAll) return;
            var name = window.Model.ActivePlaylist;
            foreach (var control in new[] { editPlaylist, deletePlaylist })
            {
                string label = window.T(control == editPlaylist ? "playlistRenameLabel" : "playlistDeleteLabel", "name", name);
                AutomationProperties.SetName(control, label);
                window.Glass.SetTip(control, label);
            }
        }

        private Grid MainHeader()
        {
            return window.Glass.Header(window.T("library"), libraryCount, delegate { window.Navigate("player"); });
        }

        private Grid SubviewHeader(string title, string detail = null)
        {
            var header = window.Glass.Header(title, detail, delegate { ShowLibrary(true); });
            var arrow = header.Children[0] as FrameworkElement;
            if (arrow != null)
            {
                AutomationProperties.SetName(arrow, window.T("backLibrary"));
                window.Glass.SetTip(arrow, window.T("backLibrary"));
            }
            return header;
        }

        private void OpenPlaylist(string name)
        {
            if (SameName(name, window.Model.ActivePlaylist)) return;
            double origin = playlistTabs[window.Model.ActivePlaylist].TranslatePoint(new Point(), tabIndicatorLayer).X;
            window.Model.SelectPlaylist(name);
            UpdatePlaylistAction();
            MoveTabIndicator(true);
            var tab = playlistTabs[name];
            tab.BringIntoView();
            var scroll = FirstVisual<ScrollViewer>(list);
            if (scroll != null) scroll.ScrollToTop();
            PanelKit.Enter(rowsHost, !window.Motion, tab.TranslatePoint(new Point(), tabIndicatorLayer).X < origin);
        }

        private FrameworkElement BuildSearch(string query)
        {
            var field = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            search = window.Glass.Field(window.T("findInLibrary"), query);
            search.Height = 40;
            search.Padding = new Thickness(36, search.Padding.Top, 31, search.Padding.Bottom);
            AutomationProperties.SetName(search, window.T("findTrack"));
            field.Children.Add(search);
            var glyph = window.Glass.Text("", 15);
            glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
            glyph.IsHitTestVisible = false;
            glyph.HorizontalAlignment = HorizontalAlignment.Left;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Margin = new Thickness(12, 0, 0, 0);
            field.Children.Add(glyph);
            var clear = window.Glass.IconButton("close", window.T("clearSearch"), delegate { search.Clear(); search.Focus(); }, 22);
            clear.HorizontalAlignment = HorizontalAlignment.Right;
            clear.VerticalAlignment = VerticalAlignment.Center;
            clear.Margin = new Thickness(0, 0, 6, 0);
            clear.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            field.Children.Add(clear);
            search.TextChanged += delegate
            {
                clear.Visibility = search.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                if (route == "library") librarySearch = search.Text;
                RefreshRows();
            };
            return field;
        }

        private static ListBox BuildList()
        {
            var row = new FrameworkElementFactory(typeof(PanelLibraryRow));
            var template = new DataTemplate(typeof(DisplayTrack)) { VisualTree = row };
            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            var containerTemplate = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = contentPresenter };
            var containers = new Style(typeof(ListBoxItem));
            containers.Setters.Add(new Setter(Control.TemplateProperty, containerTemplate));
            containers.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            containers.Setters.Add(new Setter(KeyboardNavigation.IsTabStopProperty, false));
            containers.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            var result = new ListBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0, 0, 2, 0),
                ItemTemplate = template, ItemContainerStyle = containers, Focusable = false,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScrollViewer.SetCanContentScroll(result, true);
            ScrollViewer.SetHorizontalScrollBarVisibility(result, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(result, ScrollBarVisibility.Auto);
            VirtualizingStackPanel.SetIsVirtualizing(result, true);
            VirtualizingStackPanel.SetVirtualizationMode(result, VirtualizationMode.Recycling);
            VirtualizingStackPanel.SetScrollUnit(result, ScrollUnit.Pixel);
            return result;
        }

        private void BuildTrackArea()
        {
            list = BuildList();
            rowsHost = new Grid { ClipToBounds = true, Background = Brushes.Transparent };
            rowsHost.Children.Add(list);
            content.Children.Add(rowsHost);
        }

        private string TrackCount(int count)
        {
            return window.T(count == 1 ? "trackOne" : "trackOther", "count", count.ToString(CultureInfo.CurrentCulture));
        }

        private bool IsAll { get { return SameName(window.Model.ActivePlaylist, "ALL"); } }

        internal static bool SameName(string a, string b)
        {
            return String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshRows()
        {
            if (list == null) return;
            CancelDrag();
            if (placeholder != null)
            {
                rowsHost.Children.Remove(placeholder);
                placeholder = null;
            }
            bool picking = route == "picker";
            var source = picking ? window.Model.Library : window.Model.TracksFor(window.Model.ActivePlaylist);
            string query = search.Text.Trim();
            var matching = source.Where(track => CultureInfo.CurrentCulture.CompareInfo.IndexOf(track.Title,
                query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0).ToList();
            bool canReorder = !picking && !IsAll && query.Length == 0 && matching.Count > 1;
            list.ItemsSource = matching.Select((track, index) =>
                new DisplayTrack(this, track, index, picking, canReorder, matching)).ToList();
            list.Visibility = matching.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (matching.Count == 0)
            {
                placeholder = BuildEmpty(source.Count == 0, picking);
                rowsHost.Children.Add(placeholder);
            }
            if (picking) UpdateSelected();
        }

        private FrameworkElement BuildEmpty(bool sourceEmpty, bool picking)
        {
            var state = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var note = window.Glass.Text("♫", 26);
            note.HorizontalAlignment = HorizontalAlignment.Center;
            note.Foreground = PanelKit.SubtleInk;
            note.Margin = new Thickness(0, 0, 0, 16);
            state.Children.Add(note);
            if (sourceEmpty && !picking && !IsAll)
                state.Children.Add(window.Glass.Button(window.T("addLibraryTracks"), ShowPicker, "plus"));
            else
            {
                var copy = window.Glass.Text(window.T(sourceEmpty ? "firstTrack" : "noMatchingTrack"));
                copy.Foreground = PanelKit.SubtleInk;
                copy.HorizontalAlignment = HorizontalAlignment.Center;
                state.Children.Add(copy);
                if (picking && sourceEmpty)
                {
                    var button = window.Glass.Button(window.T("addMusic"), window.OpenAddMusic, "plus");
                    button.Margin = new Thickness(0, 12, 0, 0);
                    state.Children.Add(button);
                }
            }
            return state;
        }

        private void ShowPlaylistEditor(string original)
        {
            bool renaming = original != null;
            route = renaming ? "rename" : "create";
            window.SetLibraryEditor(true);
            SwitchView(SubviewHeader(window.T(renaming ? "renamePlaylist" : "newPlaylist")), false, true);
            var body = new StackPanel { Margin = new Thickness(0, 11, 0, 0) };
            var label = window.Glass.Text(window.T("namePlaylist"), 11, true);
            label.Margin = new Thickness(0, 0, 0, 9);
            body.Children.Add(label);
            var name = window.Glass.Field(window.T("examplePlaylist"), original ?? "");
            AutomationProperties.SetName(name, window.T("namePlaylist"));
            name.MaxLength = renaming ? 120 : 64;
            name.Height = 39;
            body.Children.Add(name);
            var error = window.Glass.Text("", 11);
            error.Foreground = PanelKit.DangerInk;
            error.TextWrapping = TextWrapping.Wrap;
            error.Margin = new Thickness(0, 10, 0, 0);
            error.Visibility = Visibility.Collapsed;
            body.Children.Add(error);
            Action submit = delegate
            {
                var value = name.Text.Trim();
                if (value.Length == 0) return;
                string failure = renaming ? window.Model.RenamePlaylist(original, value) : window.Model.CreatePlaylist(value);
                if (!String.IsNullOrEmpty(failure))
                {
                    error.Text = window.T(failure);
                    error.Visibility = Visibility.Visible;
                    return;
                }
                if (!renaming) librarySearch = "";
                TryGoBack();
            };
            var save = window.Glass.Button(window.T(renaming ? "savePlaylistName" : "create"), submit);
            save.Height = 44;
            save.MinWidth = 80;
            save.IsEnabled = false;
            name.TextChanged += delegate
            {
                string candidate = name.Text.Trim();
                save.IsEnabled = candidate.Length != 0 && (renaming
                    ? !String.Equals(candidate, original, StringComparison.Ordinal) : !SameName(candidate, "ALL"));
                error.Visibility = Visibility.Collapsed;
            };
            name.KeyDown += delegate(object sender, KeyEventArgs args) { if (args.Key == Key.Enter) { args.Handled = true; if (save.IsEnabled) submit(); } };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 23, 0, 0) };
            var cancel = window.Glass.Button(window.T("cancel"), delegate { TryGoBack(); });
            cancel.Margin = new Thickness(0, 0, 9, 0);
            cancel.Height = 44;
            cancel.MinWidth = 80;
            actions.Children.Add(cancel);
            actions.Children.Add(save);
            body.Children.Add(actions);
            content.Children.Add(body);
            name.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!name.IsVisible || !window.IsActive) return;
                name.Focus();
                if (renaming) name.SelectAll();
            }));
        }

        internal bool TryGoBack()
        {
            if (route == "library") return false;
            bool focusEdit = route == "rename";
            ShowLibrary(true);
            if (focusEdit)
            {
                var button = editPlaylist;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (button.IsVisible && window.IsActive) button.Focus();
                }));
            }
            return true;
        }

        private void ShowPicker()
        {
            if (IsAll) return;
            route = "picker";
            pickerPlaylist = window.Model.ActivePlaylist;
            selected.Clear();
            SwitchView(SubviewHeader(window.T("addLibraryTracks"), pickerPlaylist), false, true);
            Dock(BuildSearch(""), System.Windows.Controls.Dock.Top);
            selectedCount = window.Glass.Text("", 11);
            selectedCount.VerticalAlignment = VerticalAlignment.Center;
            selectedCount.Foreground = PanelKit.SubtleInk;
            addSelected = window.Glass.Button(window.T("confirmPlaylistTracks"), AddSelected, "plus");
            addSelected.Height = 36;
            var footer = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(0, 9, 0, 0)
            };
            DockPanel.SetDock(addSelected, System.Windows.Controls.Dock.Right);
            footer.Children.Add(addSelected);
            footer.Children.Add(selectedCount);
            Dock(footer, System.Windows.Controls.Dock.Bottom);
            BuildTrackArea();
            RefreshRows();
        }

        private void AddSelected()
        {
            if (selected.Count == 0) return;
            List<string> names = window.Model.Library.Where(track => selected.Contains(track.Name) && !IsIncluded(track.Name))
                .Select(track => track.Name).ToList();
            if (names.Count != 0 && window.Model.SetPlaylistMembership(pickerPlaylist, names, true))
            {
                librarySearch = "";
                ShowLibrary(true);
            }
        }

        private void UpdateSelected()
        {
            selected.RemoveWhere(IsIncluded);
            int count = selected.Count;
            selectedCount.Text = window.T(count == 1 ? "selectedTrackOne" : "selectedTrackOther", "count", count.ToString(CultureInfo.CurrentCulture));
            addSelected.IsEnabled = count != 0;
        }

        private bool IsIncluded(string name)
        {
            var playlist = window.Model.Playlists.FirstOrDefault(item => SameName(item.Name, pickerPlaylist));
            return playlist != null && playlist.Tracks.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        private void RemovePlaylist()
        {
            var name = window.Model.ActivePlaylist;
            window.ConfirmRemoval(deletePlaylist, window.T("deletePlaylistQuestion"), window.T("deletePlaylistDescription"), name, () =>
            {
                if (!window.Model.DeletePlaylist(name)) return;
                ShowLibrary(false);
                PanelKit.Enter(rowsHost, !window.Motion, true);
            });
        }

        private void OnLibraryChanged()
        {
            if (route == "create" || route == "rename") return;
            if (route == "library")
            {
                string count = TrackCount(window.Model.TracksFor(window.Model.ActivePlaylist).Count);
                if (count != libraryCount)
                {
                    libraryCount = count;
                    view.Children.RemoveAt(0);
                    view.Children.Insert(0, MainHeader());
                }
            }
            RefreshRows();
        }

        private bool CommitReorder(string source, string target, bool after, double? dragTop = null)
        {
            return AnimateRowChange(() => window.Model.MoveTrack(window.Model.ActivePlaylist, source, target, after), source, dragTop);
        }

        private bool AnimateRowChange(Func<bool> update, string source = null, double? dragTop = null)
        {
            var rearrangedList = list;
            var scroll = FirstVisual<ScrollViewer>(rearrangedList);
            var oldOffset = scroll == null ? 0 : scroll.VerticalOffset;
            var positions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (window.Motion)
            {
                foreach (var row in RealizedRows(rearrangedList))
                    positions[row.Display.Track.Name] = row.TranslatePoint(new Point(), rearrangedList).Y;
                if (dragTop.HasValue) positions[source] = dragTop.Value;
            }
            if (!update()) return false;
            if (scroll != null) scroll.ScrollToVerticalOffset(oldOffset);
            var reorderedItems = rearrangedList.ItemsSource;
            if (positions.Count != 0)
                rearrangedList.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (list != rearrangedList || !list.IsLoaded || list.ItemsSource != reorderedItems) return;
                    foreach (var row in RealizedRows(list))
                    {
                        double oldY;
                        if (!positions.TryGetValue(row.Display.Track.Name, out oldY)) continue;
                        double offset = oldY - row.TranslatePoint(new Point(), list).Y;
                        if (Math.Abs(offset) >= 1) row.ShiftTo(0, true, offset);
                    }
                }));
            return true;
        }

        private static T FirstVisual<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            var match = root as T;
            if (match != null) return match;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
            {
                match = FirstVisual<T>(VisualTreeHelper.GetChild(root, index));
                if (match != null) return match;
            }
            return null;
        }

        internal sealed class DisplayTrack
        {
            internal readonly LibraryPanel View;
            internal readonly LocalTrack Track;
            internal readonly List<LocalTrack> ListedTracks;
            internal readonly bool Picking;
            internal readonly bool Reorder;
            internal readonly int Index;

            internal DisplayTrack(LibraryPanel view, LocalTrack track, int index, bool picking, bool reorder, List<LocalTrack> listed)
            {
                View = view; Track = track; Picking = picking; Reorder = reorder; Index = index; ListedTracks = listed;
            }

            internal WidgetWindow Window { get { return View.window; } }
            internal bool Included { get { return View.IsIncluded(Track.Name); } }
            internal bool Selected { get { return View.selected.Contains(Track.Name); } }
            internal bool Current { get { return Window.Model.CurrentTrack != null && SameName(Window.Model.CurrentTrack.Name, Track.Name); } }

            internal void TogglePick()
            {
                if (Included) return;
                if (!View.selected.Add(Track.Name)) View.selected.Remove(Track.Name);
                View.UpdateSelected();
            }

            internal void Play() { Window.Model.Play(Track.Name); }

            internal void Options(FrameworkElement button)
            {
                var menu = new List<PanelMenuItem>();
                foreach (var playlist in Window.Model.Playlists)
                {
                    var name = playlist.Name;
                    bool included = playlist.Tracks.Contains(Track.Name, StringComparer.OrdinalIgnoreCase);
                    menu.Add(new PanelMenuItem(Window.T(included ? "removeFrom" : "addTo", "name", name), included ? "−" : "+",
                        delegate { Window.Model.SetPlaylistMembership(name, new[] { Track.Name }, !included); }));
                }
                menu.Add(new PanelMenuItem(Window.T("deleteFile"), null, () => Window.ConfirmRemoval(button,
                    Window.T("deleteTrackQuestion"), Window.T("deleteTrackDescription"), Track.Title,
                    () => View.AnimateRowChange(() => Window.Model.DeleteTrack(Track.Name))), danger: true, icon: "trash"));
                PanelKit.Menu(Window, button, menu);
            }

            internal void StartDrag(PanelLibraryRow row, double grabY) { View.StartDrag(row, this, grabY); }

            internal void MoveByKeyboard(bool after)
            {
                int destination = Index + (after ? 1 : -1);
                if (destination < 0 || destination >= ListedTracks.Count) return;
                if (View.CommitReorder(Track.Name, ListedTracks[destination].Name, after))
                    View.RestoreGripFocus(Track.Name);
            }
        }

        private void RestoreGripFocus(string filename)
        {
            var target = list.Items.OfType<DisplayTrack>().FirstOrDefault(display => SameName(display.Track.Name, filename));
            if (target == null) return;
            list.ScrollIntoView(target);
            list.Dispatcher.BeginInvoke(new Action(delegate
            {
                var container = list.ItemContainerGenerator.ContainerFromItem(target) as DependencyObject;
                FocusGrip(container);
            }), DispatcherPriority.Loaded);
        }

        private static bool FocusGrip(DependencyObject parent)
        {
            if (parent == null) return false;
            var row = parent as PanelLibraryRow;
            if (row != null) return row.FocusGrip();
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                if (FocusGrip(VisualTreeHelper.GetChild(parent, i))) return true;
            return false;
        }
    }

    public sealed class PanelLibraryRow : Grid
    {
        private LibraryPanel.DisplayTrack track;
        private Button grip;
        private Button play;
        private TextBlock index;
        private Border active;
        private double grabY;
        private double targetOffset;
        private TranslateTransform shift;

        internal LibraryPanel.DisplayTrack Display { get { return track; } }
        internal double ReorderOffset { get { return shift == null ? 0 : shift.Y; } }

        public PanelLibraryRow()
        {
            Margin = new Thickness(0, 0, 0, 8);
            DataContextChanged += delegate { SetTrack(DataContext as LibraryPanel.DisplayTrack); };
            Loaded += delegate { Subscribe(); };
            Unloaded += delegate { Unsubscribe(); };
        }

        private void SetTrack(LibraryPanel.DisplayTrack value)
        {
            Unsubscribe();
            track = value;
            Children.Clear();
            ColumnDefinitions.Clear();
            Opacity = 1;
            if (shift != null) ShiftTo(0, false);
            grip = play = null;
            index = null;
            active = null;
            if (track == null) return;
            if (track.Picking) BuildPicker();
            else BuildRegular();
            if (IsLoaded) Subscribe();
        }

        private void Subscribe()
        {
            if (track == null || track.Picking) return;
            track.Window.Model.StateChanged -= ApplyCurrent;
            track.Window.Model.StateChanged += ApplyCurrent;
            ApplyCurrent();
        }

        private void Unsubscribe()
        {
            if (track != null && !track.Picking) track.Window.Model.StateChanged -= ApplyCurrent;
        }

        private void BuildRegular()
        {
            var window = track.Window;
            ColumnDefinitions.Add(new ColumnDefinition { Width = track.Reorder ? new GridLength(44) : new GridLength(0) });
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(43) });
            MinHeight = 44;
            play = window.Glass.Button(track.Track.Title, track.Play);
            play.Height = 44;
            play.Margin = new Thickness(track.Reorder ? 7 : 0, 0, 0, 0);
            play.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var title = window.Glass.Text(track.Track.Title, 12, true);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.VerticalAlignment = VerticalAlignment.Center;
            index = window.Glass.Text("", 11);
            index.Foreground = PanelKit.SubtleInk;
            index.Width = 18;
            index.VerticalAlignment = VerticalAlignment.Center;
            var text = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(index, System.Windows.Controls.Dock.Left);
            text.Children.Add(index);
            text.Children.Add(title);
            play.Content = text;
            AutomationProperties.SetName(play, track.Track.Title);
            Grid.SetColumn(play, 1);
            Children.Add(play);
            active = new Border
            {
                BorderBrush = PanelKit.ColorBrush(173, 229, 239, 255), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(24), IsHitTestVisible = false, Margin = play.Margin
            };
            Grid.SetColumn(active, 1);
            Children.Add(active);
            Button more = null;
            more = window.Glass.IconButton("more", window.T("trackOptions", "title", track.Track.Title),
                delegate { if (track != null) track.Options(more); }, 34);
            more.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(more, 2);
            Children.Add(more);
            if (track.Reorder) MakeGrip();
            ApplyCurrent();
        }

        private void BuildPicker()
        {
            var window = track.Window;
            MinHeight = 46;
            var grid = new DockPanel { Margin = new Thickness(12, 5, 7, 5), LastChildFill = true };
            var note = window.Glass.Text("♫", 17);
            note.Foreground = PanelKit.SubtleInk;
            note.Margin = new Thickness(0, 0, 9, 0);
            note.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(note, System.Windows.Controls.Dock.Left);
            grid.Children.Add(note);
            var select = window.Glass.IconButton(track.Included || track.Selected ? "check" : "plus",
                window.T(track.Included ? "trackAlreadyIncluded" : "selectLibraryTrack", "title", track.Track.Title),
                delegate { track.TogglePick(); BuildPickerSelection(grid); }, 34);
            select.IsEnabled = !track.Included;
            select.Tag = "selection";
            DockPanel.SetDock(select, System.Windows.Controls.Dock.Right);
            grid.Children.Add(select);
            var title = window.Glass.Text(track.Track.Title, 12, true);
            title.VerticalAlignment = VerticalAlignment.Center;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            grid.Children.Add(title);
            var surface = window.Glass.Popover(grid, 14, embedded: true);
            surface.Padding = new Thickness(0);
            active = surface;
            Children.Add(surface);
            UpdatePickerRim();
        }

        private void BuildPickerSelection(DockPanel grid)
        {
            var old = grid.Children.OfType<Button>().FirstOrDefault(button => Equals(button.Tag, "selection"));
            if (old == null) return;
            int position = grid.Children.IndexOf(old);
            grid.Children.Remove(old);
            var window = track.Window;
            var current = window.Glass.IconButton(track.Selected ? "check" : "plus",
                window.T("selectLibraryTrack", "title", track.Track.Title), delegate { track.TogglePick(); BuildPickerSelection(grid); }, 34);
            current.Tag = "selection";
            DockPanel.SetDock(current, System.Windows.Controls.Dock.Right);
            grid.Children.Insert(position, current);
            current.Focus();
            UpdatePickerRim();
        }

        private void UpdatePickerRim()
        {
            active.BorderBrush = track.Included || track.Selected ? PanelKit.ColorBrush(173, 229, 239, 255) : null;
        }

        private void ApplyCurrent()
        {
            if (index == null || track == null) return;
            bool current = track.Current;
            index.Text = current ? (track.Window.Model.IsPlaying ? "Ⅱ" : "▸") : (track.Index + 1).ToString(CultureInfo.CurrentCulture);
            active.Visibility = current ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MakeGrip()
        {
            var window = track.Window;
            grip = window.Glass.IconButton("grip", window.T("moveTrackLabel", "title", track.Track.Title), delegate { }, 44);
            window.Glass.SetTip(grip, window.T("reorderHint"));
            grip.Cursor = Cursors.Arrow;
            Children.Add(grip);
            grip.PreviewMouseLeftButtonDown += (sender, args) => grabY = args.GetPosition(this).Y;
            grip.PreviewMouseMove += delegate(object sender, MouseEventArgs args)
            {
                if (track == null || !grip.IsMouseCaptured || args.LeftButton != MouseButtonState.Pressed) return;
                if (Math.Abs(args.GetPosition(this).Y - grabY) >= SystemParameters.MinimumVerticalDragDistance)
                    track.StartDrag(this, grabY);
            };
            grip.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key != Key.Up && args.Key != Key.Down) return;
                args.Handled = true;
                track.MoveByKeyboard(args.Key == Key.Down);
            };
        }

        internal bool FocusGrip() { return grip != null && grip.Focus(); }

        internal void ShiftTo(double offset, bool animate, double? start = null)
        {
            if (shift == null)
            {
                if (offset == 0 && !start.HasValue) return;
                shift = new TranslateTransform();
                RenderTransform = shift;
            }
            if (animate && !start.HasValue && targetOffset == offset) return;
            double from = start ?? shift.Y;
            targetOffset = offset;
            shift.Y = offset;
            shift.BeginAnimation(TranslateTransform.YProperty, animate && from != offset
                ? Mini.Glass.Transition(from, offset, 200, Mini.Glass.Ease) : null);
        }
    }
}
