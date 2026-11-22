using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MusicWidget.NativeMini;

namespace MusicWidget.NativeApp
{
    internal sealed class OnlinePanel : UserControl, IDisposable
    {
        private const int PageSize = 8;
        private readonly WidgetWindow window;
        private readonly PlayerModel model;
        private readonly OnlinePreview preview;
        private readonly OnlineArtwork thumbnails = new OnlineArtwork();
        private readonly DispatcherTimer searchTimer;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly List<SearchRow> rows = new List<SearchRow>();
        private readonly List<TrackSearchItem> results = new List<TrackSearchItem>();
        private Grid root;
        private Grid modal;
        private Grid pagination;
        private TextBox queryField;
        private Button searchButton, previousButton, nextButton, linkButton;
        private TextBlock summary, previewSummary, resultError, pageLabel;
        private ScrollViewer resultScroll;
        private StackPanel resultList;
        private Border downloadCard, toolsCard;
        private TextBlock downloadCaption, downloadSize, toolsCaption;
        private Button enableButton;
        private OnlineProgressBar downloadBar, toolsBar;
        private TrackSearchRequest searchRequest;
        private string queryDraft = "", query = "", linkDraft = "", searchError;
        private int page = 1;
        private bool searchTab = true, hasNext, downloading, installing, installFailed, downloadError, disposed;
        private string downloadStatus, downloadPlaylist;
        private long downloadReceived, downloadTotal;
        private OnlineInstallProgress installProgress;
        private string Executable { get { return Path.Combine(model.ToolsDirectory, "yt-dlp.exe"); } }

        public OnlinePanel(WidgetWindow window)
        {
            this.window = window;
            model = window.Model;
            preview = new OnlinePreview(model);
            preview.Changed += UpdateSearchControls;
            searchTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            searchTimer.Tick += SearchCompleted;
            model.StateChanged += UpdateSearchControls;
            model.PlaylistRenamed += FollowPlaylistRename;
            Build();
        }

        public void ShowTab(bool search)
        {
            if (searchTab != search) OnNavigateAway();
            searchTab = search;
            Build();
        }

        public void OnNavigateAway()
        {
            CloseDestination();
            CancelSearch();
            StopPreview(true);
        }

        public void StopPreview(bool resume)
        {
            preview.Stop(resume);
        }

        public bool TryDismiss()
        {
            if (modal == null) return false;
            CloseDestination();
            return true;
        }

        public void RefreshLanguage()
        {
            Build();
        }

        private string T(string key)
        {
            return window.T(key);
        }

        private string T(string key, string placeholder, string value)
        {
            return window.T(key, placeholder, value);
        }

        private void Build(int direction = 0)
        {
            CloseDestination();
            root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(window.Glass.Header(T("addMusic")));
            var tabTrack = new GlassSegments(window, T("musicSources"), T("search"), T("linkOrFile"),
                searchTab ? 0 : 1, value => SwitchTab(value == 0), "search", "link");
            tabTrack.Margin = new Thickness(0, 0, 0, 15);
            Grid.SetRow(tabTrack, 1);
            root.Children.Add(tabTrack);

            queryField = null;
            searchButton = null;
            linkButton = null;
            rows.Clear();
            var content = searchTab ? BuildSearch() : BuildImport();
            Grid.SetRow(content, 2);
            root.Children.Add(content);

            downloadCaption = window.Glass.Text("", 11, true);
            downloadCaption.TextWrapping = TextWrapping.Wrap;
            downloadSize = window.Glass.Hint("", 10);
            downloadSize.Margin = new Thickness(0, 5, 0, 0);
            downloadBar = new OnlineProgressBar(window.Glass.Aura, model.Settings.ReducedMotion);
            downloadBar.Margin = new Thickness(0, 6, 0, 0);
            var feedback = new StackPanel();
            feedback.Children.Add(downloadCaption);
            feedback.Children.Add(downloadBar);
            feedback.Children.Add(downloadSize);
            downloadCard = window.Glass.Popover(feedback, 14);
            downloadCard.Margin = new Thickness(0, 12, 0, 0);
            downloadCard.Padding = new Thickness(13, 10, 13, 10);
            Grid.SetRow(downloadCard, 3);
            root.Children.Add(downloadCard);
            Content = root;
            if (direction != 0)
            {
                PanelKit.Enter(content, !window.Motion, direction < 0);
                tabTrack.EnterFrom(searchTab ? 1 : 0);
            }
            UpdateFeedback();
        }

        private void SwitchTab(bool search)
        {
            if (search == searchTab) return;
            OnNavigateAway();
            searchTab = search;
            Build(search ? -1 : 1);
        }

        private FrameworkElement BuildSearch()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.Children.Add(BuildToolsCard());
            var completeSearch = new Grid { Visibility = OnlineToolInstaller.IsAvailable(model.ToolsDirectory) ? Visibility.Visible : Visibility.Collapsed };
            completeSearch.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            completeSearch.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            completeSearch.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            completeSearch.RowDefinitions.Add(new RowDefinition());
            completeSearch.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            completeSearch.Children.Add(BuildSearchForm());

            var status = new Grid { Margin = new Thickness(0, 16, 0, 10) };
            status.ColumnDefinitions.Add(new ColumnDefinition());
            status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            summary = window.Glass.Hint("", 10);
            summary.TextWrapping = TextWrapping.NoWrap;
            summary.TextTrimming = TextTrimming.CharacterEllipsis;
            status.Children.Add(summary);
            previewSummary = window.Glass.Text("", 10);
            previewSummary.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(previewSummary, 1);
            status.Children.Add(previewSummary);
            Grid.SetRow(status, 1);
            completeSearch.Children.Add(status);

            resultError = window.Glass.Hint("");
            resultError.Foreground = PanelKit.DangerInk;
            resultError.Margin = new Thickness(3, 0, 3, 10);
            Grid.SetRow(resultError, 2);
            completeSearch.Children.Add(resultError);

            resultList = new StackPanel();
            resultScroll = PanelKit.Scroll(resultList);
            Grid.SetRow(resultScroll, 3);
            completeSearch.Children.Add(resultScroll);
            pagination = BuildPagination();
            Grid.SetRow(pagination, 4);
            completeSearch.Children.Add(pagination);
            Grid.SetRow(completeSearch, 1);
            grid.Children.Add(completeSearch);
            RenderResults();
            UpdateSearchControls();
            return grid;
        }

        private FrameworkElement BuildSearchForm()
        {
            var form = new StackPanel();
            var searchField = new Grid();
            queryField = window.Glass.Field(T("searchPlaceholder"), queryDraft);
            queryField.MaxLength = 200;
            queryField.Padding = new Thickness(35, 6, 38, 6);
            AutomationProperties.SetName(queryField, T("trackOrArtist"));
            queryField.TextChanged += (sender, args) =>
            {
                queryDraft = queryField.Text;
                UpdateSearchControls();
            };
            queryField.KeyDown += (sender, args) =>
            {
                if (args.Key == Key.Enter && searchButton.IsEnabled)
                {
                    args.Handled = true;
                    StartSearch(queryDraft, 1);
                }
            };
            searchField.Children.Add(queryField);
            var glyph = window.Glass.Glyph("search", 15);
            glyph.Margin = new Thickness(12, 0, 0, 0);
            glyph.HorizontalAlignment = HorizontalAlignment.Left;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            searchField.Children.Add(glyph);
            var clear = window.Glass.IconButton("close", T("clearSearch"), () =>
            {
                queryField.Clear();
                queryField.Focus();
                CancelSearch();
                StopPreview(true);
                query = "";
                hasNext = false;
                page = 1;
                results.Clear();
                RenderResults();
                UpdateSearchControls();
            }, 24);
            clear.Margin = new Thickness(0, 0, 6, 0);
            clear.VerticalAlignment = VerticalAlignment.Center;
            clear.HorizontalAlignment = HorizontalAlignment.Right;
            clear.Visibility = String.IsNullOrEmpty(queryDraft) ? Visibility.Collapsed : Visibility.Visible;
            queryField.TextChanged += (sender, args) =>
                clear.Visibility = String.IsNullOrEmpty(queryField.Text) ? Visibility.Collapsed : Visibility.Visible;
            searchField.Children.Add(clear);
            form.Children.Add(searchField);

            searchButton = window.Glass.Button(T("search"), () => StartSearch(queryDraft, 1), "search");
            searchButton.Margin = new Thickness(0, 9, 0, 0);
            form.Children.Add(searchButton);
            return form;
        }

        private Grid BuildPagination()
        {
            var bar = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            AutomationProperties.SetName(bar, T("resultPages"));
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            previousButton = window.Glass.Button(T("previousPage"), () => StartSearch(query, page - 1), "back");
            previousButton.MinHeight = 30;
            bar.Children.Add(previousButton);
            pageLabel = window.Glass.Text("", 11);
            pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(pageLabel, 1);
            bar.Children.Add(pageLabel);
            nextButton = window.Glass.Button(T("nextPage"), () => StartSearch(query, page + 1));
            nextButton.MinHeight = 30;
            Grid.SetColumn(nextButton, 2);
            bar.Children.Add(nextButton);
            return bar;
        }

        private FrameworkElement BuildImport()
        {
            var stack = new StackPanel { Margin = new Thickness(1, 1, 1, 0) };
            var local = window.Glass.Button(T("pickFiles"), ImportFiles);
            local.MinHeight = 77;
            local.HorizontalContentAlignment = HorizontalAlignment.Left;
            local.Padding = new Thickness(16);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = window.Glass.Glyph("upload", 22);
            icon.Margin = new Thickness(0, 0, 13, 0);
            row.Children.Add(icon);
            var copy = new StackPanel();
            copy.Children.Add(window.Glass.Text(T("pickFiles"), 12, true));
            var hint = window.Glass.Hint("MP3, WAV, FLAC, M4A…", 10);
            hint.Margin = new Thickness(0, 6, 0, 0);
            copy.Children.Add(hint);
            row.Children.Add(copy);
            local.Content = row;
            stack.Children.Add(local);

            var divider = new Grid { Margin = new Thickness(0, 24, 0, 24) };
            divider.ColumnDefinitions.Add(new ColumnDefinition());
            divider.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            divider.ColumnDefinitions.Add(new ColumnDefinition());
            var separator = window.Glass.Hint(T("orFromLink"), 10);
            separator.Margin = new Thickness(10, 0, 10, 0);
            Grid.SetColumn(separator, 1);
            divider.Children.Add(separator);
            var line = PanelKit.ColorBrush(36, 226, 236, 255);
            for (int column = 0; column < 3; column += 2)
            {
                var rule = new Border { Height = 1, Background = line, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(rule, column);
                divider.Children.Add(rule);
            }
            stack.Children.Add(divider);
            stack.Children.Add(BuildToolsCard());
            if (OnlineToolInstaller.IsAvailable(model.ToolsDirectory))
            {
                var caption = window.Glass.Text(T("youtubeSpotifyLink"), 11, true);
                caption.Margin = new Thickness(4, 0, 0, 7);
                stack.Children.Add(caption);
                var input = window.Glass.Field("https://…", linkDraft);
                input.MaxLength = 4096;
                AutomationProperties.SetName(input, T("youtubeSpotifyLink"));
                input.TextChanged += (sender, args) => { linkDraft = input.Text; UpdateFeedback(); };
                input.KeyDown += (sender, args) =>
                {
                    if (args.Key == Key.Enter && linkButton.IsEnabled)
                    {
                        args.Handled = true;
                        SelectDestination(linkDraft, "");
                    }
                };
                stack.Children.Add(input);
                linkButton = window.Glass.Button(T("addToLibrary"), () => SelectDestination(linkDraft, ""), "link");
                linkButton.MinHeight = 44;
                linkButton.Margin = new Thickness(0, 13, 0, 0);
                stack.Children.Add(linkButton);
            }
            return PanelKit.Scroll(stack);
        }

        private FrameworkElement BuildToolsCard()
        {
            toolsCaption = window.Glass.Hint("");
            var content = new StackPanel();
            content.Children.Add(toolsCaption);
            toolsBar = new OnlineProgressBar(window.Glass.Aura, model.Settings.ReducedMotion);
            toolsBar.Margin = new Thickness(0, 8, 0, 0);
            content.Children.Add(toolsBar);
            enableButton = window.Glass.Button(T("enableOnlineTools"), StartTools, "download");
            enableButton.Margin = new Thickness(0, 9, 0, 0);
            content.Children.Add(enableButton);
            toolsCard = window.Glass.Popover(content, 14);
            toolsCard.Padding = new Thickness(12);
            toolsCard.Margin = new Thickness(0, 0, 0, 12);
            return toolsCard;
        }

        private async void StartTools()
        {
            if (installing || downloading || !Environment.Is64BitOperatingSystem) return;
            installing = true;
            installFailed = false;
            installProgress = null;
            UpdateFeedback();
            var progress = new Progress<OnlineInstallProgress>(value =>
            {
                if (disposed) return;
                installProgress = value;
                UpdateFeedback();
            });
            try { await OnlineToolInstaller.InstallAsync(model.ToolsDirectory, progress, lifetime.Token); }
            catch (Exception) { installFailed = true; }
            finally
            {
                installing = false;
                installProgress = null;
                if (!disposed && !installFailed && window.Pane == "add") Build();
                else if (!disposed) UpdateFeedback();
            }
        }

        private void ImportFiles()
        {
            var dialog = new OpenFileDialog
            {
                Title = T("pickFiles"), Multiselect = true, CheckFileExists = true,
                Filter = T("audioFiles") + "|*.mp3;*.wav;*.m4a;*.flac;*.aac;*.wma;*.ogg|" + T("allFiles") + "|*.*"
            };
            if (dialog.ShowDialog(window) != true) return;
            try
            {
                if (model.ImportFiles(dialog.FileNames) > 0)
                {
                    window.Navigate("library");
                    return;
                }
                if (!downloading) SetDownloadFeedback("addFailed", true, 0, 0);
            }
            catch (Exception) { if (!downloading) SetDownloadFeedback("addFailed", true, 0, 0); }
        }

        private void StartSearch(string input, int number)
        {
            var term = (input ?? "").Trim();
            if (term.Length < 2 || number < 1 || number > 10 || !OnlineToolInstaller.IsAvailable(model.ToolsDirectory)) return;
            CancelSearch();
            StopPreview(true);
            query = term;
            page = number;
            hasNext = false;
            results.Clear();
            try
            {
                searchRequest = TrackSearch.TaskStart(term, number, PageSize, Executable);
                searchTimer.Start();
            }
            catch (ArgumentException) { searchError = "invalidSearch"; }
            catch (Exception) { searchError = "searchStartFailed"; }
            RenderResults();
            UpdateSearchControls();
        }

        private void SearchCompleted(object sender, EventArgs args)
        {
            if (searchRequest == null || !searchRequest.IsCompleted) return;
            var outcome = searchRequest.Result;
            CancelSearch();
            if (outcome == null) searchError = "searchDisplayFailed";
            else if (!outcome.Cancelled)
            {
                searchError = outcome.Error;
                hasNext = outcome.HasNext;
                results.AddRange(outcome.Items);
            }
            RenderResults();
            UpdateSearchControls();
        }

        private void CancelSearch()
        {
            searchTimer.Stop();
            if (searchRequest != null) { searchRequest.Dispose(); searchRequest = null; }
            searchError = null;
        }

        private void RenderResults()
        {
            if (!searchTab || resultList == null) return;
            rows.Clear();
            resultList.Children.Clear();
            if (searchRequest != null)
            {
                for (int index = 0; index < 4; ++index)
                    resultList.Children.Add(new Border { Background = PanelKit.Tint(window.Glass.Aura, 25), CornerRadius = new CornerRadius(14),
                        Height = 76, Margin = new Thickness(0, 0, 0, 9) });
            }
            else if (results.Count == 0 && String.IsNullOrEmpty(searchError))
            {
                var empty = new StackPanel { Margin = new Thickness(10, 42, 10, 12) };
                var icon = window.Glass.Glyph(query.Length == 0 ? "search" : "note", 30);
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Children.Add(icon);
                var copy = window.Glass.Hint(T(query.Length == 0 ? "searchStart" : "noSearchResults"), 12);
                copy.Margin = new Thickness(0, 14, 0, 0);
                copy.TextAlignment = TextAlignment.Center;
                empty.Children.Add(copy);
                resultList.Children.Add(empty);
            }
            else foreach (var result in results) resultList.Children.Add(BuildResult(result));
            resultScroll.ScrollToTop();
        }

        private sealed class SearchRow
        {
            public TrackSearchItem Item;
            public GlassButton Play;
            public Button Download;
            public string PlayIcon = "play";
        }

        private FrameworkElement BuildResult(TrackSearchItem item)
        {
            var grid = new Grid { Margin = new Thickness(12, 9, 11, 9), MinHeight = 56 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var artwork = new Grid { Width = 56, Height = 48, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
            artwork.Children.Add(window.Glass.Glyph("note", 25));
            var image = new Image { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
            artwork.Children.Add(image);
            var play = window.Glass.IconButton("play", T("playPreviewOf", "title", item.Title), () => preview.Toggle(item, Executable), 32);
            play.HorizontalAlignment = HorizontalAlignment.Left;
            play.VerticalAlignment = VerticalAlignment.Center;
            window.Glass.SetTip(play, T("playPreview"));
            grid.Children.Add(play);
            var artworkFrame = PanelKit.Track(artwork, 9);
            artworkFrame.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(artworkFrame, 1);
            grid.Children.Add(artworkFrame);
            ApplyThumbnail(image, item.Id);

            var metadata = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
            var title = window.Glass.Text(item.Title, 12, true);
            title.TextWrapping = TextWrapping.Wrap;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.LineHeight = 17;
            title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            title.MaxHeight = 34;
            window.Glass.SetTip(title, item.Title);
            metadata.Children.Add(title);
            string artist = String.IsNullOrEmpty(item.Artist) ? T("unknownArtist") : item.Artist;
            string duration = item.IsLive ? T("live") : item.DurationLabel;
            var detail = window.Glass.Hint(artist + (String.IsNullOrEmpty(duration) ? "" : " · " + duration), 10);
            detail.TextWrapping = TextWrapping.NoWrap;
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            detail.Margin = new Thickness(0, 4, 0, 0);
            metadata.Children.Add(detail);
            Grid.SetColumn(metadata, 2);
            grid.Children.Add(metadata);

            var download = window.Glass.IconButton("download", T("addTrackToLibrary", "title", item.Title),
                () => SelectDestination(item.Url, item.Title), 32);
            download.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(download, 3);
            grid.Children.Add(download);
            rows.Add(new SearchRow { Item = item, Play = play, Download = download });
            var material = window.Glass.Popover(grid, 14, embedded: true);
            material.Margin = new Thickness(0, 0, 4, 9);
            material.Padding = new Thickness(0);
            return material;
        }

        private async void ApplyThumbnail(Image image, string id)
        {
            var source = await thumbnails.GetAsync(id, lifetime.Token);
            if (!disposed && source != null) image.Source = source;
        }

        private void UpdateSearchControls()
        {
            if (disposed || !searchTab || queryField == null || previousButton == null) return;
            bool busy = searchRequest != null;
            var term = queryDraft.Trim();
            bool activeSearch = busy && String.Equals(term, query, StringComparison.Ordinal);
            searchButton.IsEnabled = !activeSearch && term.Length >= 2;
            string current = T(activeSearch ? "searching" : "search");
            var content = (StackPanel)searchButton.Content;
            ((TextBlock)content.Children[content.Children.Count - 1]).Text = current;
            AutomationProperties.SetName(searchButton, current);
            summary.Text = busy ? T("searchProgress") : (query.Length == 0 ? T("searchYouTube") : T("searchResults", "query", query));
            previewSummary.Text = preview.IsLoading ? T("previewLoading") : (preview.IsPlaying ?
                T(model.Settings.IsMuted || model.Settings.LocalVolume == 0 ? "previewMuted" : "previewPlaying") : "");
            resultError.Text = String.IsNullOrEmpty(searchError) ? (String.IsNullOrEmpty(preview.Error) ? "" : T(preview.Error)) : T(searchError);
            resultError.Visibility = resultError.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            pagination.Visibility = query.Length > 0 && String.IsNullOrEmpty(searchError) ? Visibility.Visible : Visibility.Collapsed;
            pageLabel.Text = T("pageNumber", "page", page.ToString(CultureInfo.CurrentCulture));
            previousButton.IsEnabled = !busy && page > 1;
            nextButton.IsEnabled = !busy && hasNext && page < 10;
            foreach (var row in rows)
            {
                bool selected = preview.IsActive && preview.TrackId == row.Item.Id;
                string icon = selected && preview.IsLoading ? "close" : selected && preview.IsPlaying ? "pause" : "play";
                if (icon != row.PlayIcon)
                {
                    row.PlayIcon = icon;
                    row.Play.SetIcon(icon);
                    string action = icon == "close" ? "stopPreview" : icon == "pause" ? "pausePreview" : "playPreview";
                    AutomationProperties.SetName(row.Play, T(action + "Of", "title", row.Item.Title));
                    window.Glass.SetTip(row.Play, T(action));
                }
                row.Download.IsEnabled = !downloading;
            }
        }

        private void SelectDestination(string rawUrl, string title)
        {
            if (downloading) return;
            Uri url;
            if (!TryLink(rawUrl, out url)) { SetDownloadFeedback("invalidLink", true, 0, 0); return; }
            var playlists = model.Playlists.Select(item => item.Name).ToArray();
            if (playlists.Length == 0) { StartDownload(url, null); return; }

            CloseDestination();
            modal = new Grid { Background = PanelKit.ColorBrush(160, 8, 10, 16) };
            Grid.SetRowSpan(modal, 4);
            KeyboardNavigation.SetTabNavigation(modal, KeyboardNavigationMode.Cycle);
            var card = new StackPanel { Margin = new Thickness(12) };
            var heading = new DockPanel { LastChildFill = true };
            var close = window.Glass.IconButton("close", T("cancel"), CloseDestination, 29);
            DockPanel.SetDock(close, Dock.Right);
            heading.Children.Add(close);
            heading.Children.Add(window.Glass.Text(T("downloadDestination"), 17, true));
            card.Children.Add(heading);
            var description = window.Glass.Hint(String.IsNullOrWhiteSpace(title) ? T("downloadDestinationDescription") : title);
            description.Margin = new Thickness(0, 6, 0, 16);
            card.Children.Add(description);
            var label = window.Glass.Text(T("saveTo"), 11, true);
            label.Margin = new Thickness(0, 0, 0, 7);
            card.Children.Add(label);
            string destination = null;
            var selectSlot = new ContentControl();
            var choices = new List<PanelMenuItem>();
            Action refresh = () => selectSlot.Content = PanelKit.Choice(window, destination ?? T("all"), choices, matchWidth: true);
            choices.Add(new PanelMenuItem(T("all"), "", () => { destination = null; refresh(); }));
            foreach (string name in playlists)
            {
                string choice = name;
                choices.Add(new PanelMenuItem(choice, "", () => { destination = choice; refresh(); }));
            }
            refresh();
            card.Children.Add(selectSlot);
            var actions = PanelKit.Split(window.Glass.Button(T("cancel"), CloseDestination),
                window.Glass.Button(T("downloadTrack"), () => { CloseDestination(); StartDownload(url, destination); }, "download"));
            actions.Margin = new Thickness(0, 17, 0, 0);
            card.Children.Add(actions);
            var frame = window.Glass.Popover(card, 23);
            frame.MaxWidth = 365;
            frame.Width = Math.Min(365, Math.Max(240, ActualWidth - 18));
            frame.HorizontalAlignment = HorizontalAlignment.Center;
            frame.VerticalAlignment = VerticalAlignment.Center;
            modal.Children.Add(frame);
            root.Children.Add(modal);
            close.Focus();
        }

        private static bool TryLink(string text, out Uri url)
        {
            var value = (text ?? "").Trim();
            if (value.Length > 4096 || !Uri.TryCreate(value, UriKind.Absolute, out url))
            {
                url = null;
                return false;
            }
            if ((url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps) || !url.IsDefaultPort ||
                !String.IsNullOrEmpty(url.UserInfo))
                return false;
            string host = url.DnsSafeHost.ToLowerInvariant();
            if (host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal) ||
                host == "youtu.be" || host == "www.youtu.be") return true;
            return host == "open.spotify.com" &&
                Regex.IsMatch(url.AbsolutePath, @"^/(?:intl-[A-Za-z-]+/)?track/[A-Za-z0-9]{22}/?$", RegexOptions.CultureInvariant);
        }

        private void CloseDestination()
        {
            if (modal == null) return;
            root.Children.Remove(modal);
            modal = null;
        }

        private void StartDownload(Uri source, string destination)
        {
            if (downloading) return;
            if (!OnlineToolInstaller.IsAvailable(model.ToolsDirectory)) { SetDownloadFeedback("downloadInitFailed", true, 0, 0); return; }
            downloading = true;
            downloadPlaylist = destination;
            SetDownloadFeedback("downloadStarting", false, 0, 0);
            try
            {
                TrackDownloader.StartDownload(source.AbsoluteUri, Executable, model.MusicDirectory, Dispatcher,
                    (key, received, total) => { if (!disposed) SetDownloadFeedback(key, false, received, total); },
                    FinishDownload);
            }
            catch (Exception)
            {
                downloading = false;
                downloadPlaylist = null;
                SetDownloadFeedback("downloadInitFailed", true, 0, 0);
            }
        }

        private void FollowPlaylistRename(string previous, string name)
        {
            if (String.Equals(downloadPlaylist, previous, StringComparison.OrdinalIgnoreCase)) downloadPlaylist = name;
        }

        private void FinishDownload(bool success, string result)
        {
            if (disposed) return;
            downloading = false;
            string destination = downloadPlaylist;
            downloadPlaylist = null;
            if (!success) { SetDownloadFeedback(result, true, 0, 0); return; }
            string status = "downloadSuccess";
            try
            {
                model.RefreshLibrary();
                if (destination != null)
                    status = model.SetPlaylistMembership(destination, new[] { Path.GetFileName(result) }, true) ?
                        "downloadSavedToPlaylist" : "downloadPlaylistUnavailable";
            }
            catch (Exception) { if (destination != null) status = "downloadPlaylistUnavailable"; }
            SetDownloadFeedback(status, false, 0, 0);
        }

        private void SetDownloadFeedback(string key, bool error, long received, long total)
        {
            downloadStatus = key;
            downloadError = error;
            downloadReceived = received;
            downloadTotal = total;
            UpdateFeedback();
        }

        private void UpdateFeedback()
        {
            if (disposed || downloadCard == null || toolsCard == null) return;
            var tools = OnlineToolInstaller.IsComplete(model.ToolsDirectory);
            toolsCard.Visibility = tools ? Visibility.Collapsed : Visibility.Visible;
            toolsCaption.Text = installing ? T("installingOnlineTools") : T(installFailed ? "onlineToolsInstallFailed" :
                (Environment.Is64BitOperatingSystem ? "onlineToolsSetupHint" : "onlineToolsUnavailable"));
            if (installing && installProgress != null) toolsCaption.Text += " " + installProgress.Tool + " · " +
                FormatBytes(installProgress.BytesReceived) + " / " + FormatBytes(installProgress.TotalBytes);
            toolsCaption.Foreground = installFailed ? PanelKit.DangerInk : PanelKit.SubtleInk;
            enableButton.Visibility = !installing && Environment.Is64BitOperatingSystem ? Visibility.Visible : Visibility.Collapsed;
            enableButton.IsEnabled = !downloading;
            toolsBar.Update(installing, installProgress == null || installProgress.Verifying ? 0 : installProgress.TotalBytes,
                installProgress == null ? 0 : installProgress.BytesReceived);
            downloadCard.Visibility = String.IsNullOrEmpty(downloadStatus) ? Visibility.Collapsed : Visibility.Visible;
            downloadCaption.Text = String.IsNullOrEmpty(downloadStatus) ? "" : T(downloadStatus);
            downloadCaption.Foreground = downloadError ? PanelKit.DangerInk :
                downloading ? PanelKit.SubtleInk : PanelKit.ColorBrush(255, 171, 239, 192);
            downloadSize.Visibility = downloading && (downloadReceived > 0 || downloadTotal > 0) ? Visibility.Visible : Visibility.Collapsed;
            downloadSize.Text = T(downloadTotal > 0 ? "downloadByteCount" : "downloadReceived", "received",
                FormatBytes(downloadReceived)).Replace("{total}", FormatBytes(downloadTotal));
            downloadBar.Update(downloading, downloadTotal, downloadReceived);
            if (linkButton != null) linkButton.IsEnabled = !downloading && !String.IsNullOrWhiteSpace(linkDraft);
            UpdateSearchControls();
        }

        private static string FormatBytes(long bytes)
        {
            bool megabytes = bytes >= 1000000;
            return (bytes / (megabytes ? 1000000d : 1000d)).ToString("0.#", CultureInfo.CurrentCulture) + (megabytes ? " MB" : " kB");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CancelSearch();
            model.StateChanged -= UpdateSearchControls;
            model.PlaylistRenamed -= FollowPlaylistRename;
            preview.Changed -= UpdateSearchControls;
            preview.Dispose();
            lifetime.Cancel();
            lifetime.Dispose();
        }
    }

    internal sealed class OnlineProgressBar : Grid
    {
        private readonly Border fill;
        private readonly ScaleTransform scale;
        private readonly bool reducedMotion;
        private bool indeterminate;

        public OnlineProgressBar(Color aura, bool reducedMotion)
        {
            this.reducedMotion = reducedMotion;
            Height = 8;
            ClipToBounds = true;
            Children.Add(new Border { Background = PanelKit.ColorBrush(118, 9, 10, 17), CornerRadius = new CornerRadius(4) });
            fill = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new LinearGradientBrush(Glass.Mix(aura, Colors.White, .72), Glass.Mix(aura, Colors.White, .26), 90)
            };
            scale = new ScaleTransform(0, 1);
            fill.RenderTransform = scale;
            Children.Add(fill);
            Visibility = Visibility.Collapsed;
        }

        public void Update(bool visible, long total, long received)
        {
            Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            scale.ScaleX = total > 0 ? Math.Max(0, Math.Min(1, (double)received / total)) : .36;
            bool animate = visible && total <= 0 && !reducedMotion;
            if (indeterminate == animate) return;
            indeterminate = animate;
            fill.BeginAnimation(OpacityProperty, animate ? new DoubleAnimation(.38, .9, TimeSpan.FromMilliseconds(700))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        }
    }

    internal sealed class OnlineArtwork
    {
        private static readonly Regex ValidId = new Regex("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant);
        private readonly SemaphoreSlim simultaneous = new SemaphoreSlim(3);
        private readonly Dictionary<string, Task<BitmapSource>> cache = new Dictionary<string, Task<BitmapSource>>(StringComparer.Ordinal);
        private readonly Queue<string> order = new Queue<string>();

        public Task<BitmapSource> GetAsync(string id, CancellationToken cancellation)
        {
            if (id == null || !ValidId.IsMatch(id)) return Task.FromResult<BitmapSource>(null);
            Task<BitmapSource> existing;
            if (cache.TryGetValue(id, out existing)) return existing;
            while (order.Count >= 48) cache.Remove(order.Dequeue());
            var pending = FetchAsync(id, cancellation);
            order.Enqueue(id);
            cache.Add(id, pending);
            return pending;
        }

        private async Task<BitmapSource> FetchAsync(string id, CancellationToken cancellation)
        {
            bool entered = false;
            try
            {
                await simultaneous.WaitAsync(cancellation).ConfigureAwait(false);
                entered = true;
                return await Task.Run(() =>
                {
                    using (var data = new MemoryStream())
                    {
                        OnlineHttp.Read(new Uri("https://i.ytimg.com/vi/" + id + "/default.jpg"),
                            host => host == "i.ytimg.com", 4500, "image/jpeg", cancellation, (response, stream) =>
                            {
                                if (response.ContentType == null || !response.ContentType.StartsWith("image/jpeg", StringComparison.OrdinalIgnoreCase))
                                    throw new InvalidDataException();
                                OnlineHttp.CopyBounded(stream, data, 256 * 1024, cancellation, null);
                            });
                        data.Position = 0;
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.DecodePixelWidth = 120;
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = data;
                        image.EndInit();
                        image.Freeze();
                        return (BitmapSource)image;
                    }
                }, cancellation).ConfigureAwait(false);
            }
            catch (Exception) { return null; }
            finally { if (entered) simultaneous.Release(); }
        }
    }
}
