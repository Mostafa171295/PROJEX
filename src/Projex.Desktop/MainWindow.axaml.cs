using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Projex.Desktop;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PROJEX", "desktop-settings.json");
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private string? _accessToken;
    private string? _refreshToken;
    private string? _tenantKey;
    private Uri? _serverAddress;
    private ProjectItem? _selectedProject;
    private List<ProjectItem> _projects = [];

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBox>("ServerUrlBox")!.Text = LoadServerAddress();
        this.FindControl<Button>("LoginButton")!.Click += async (_, _) => await LoginAsync();
        this.FindControl<Button>("LogoutButton")!.Click += async (_, _) => await LogoutAsync();
        this.FindControl<Button>("DashboardButton")!.Click += (_, _) => ShowPage("dashboard");
        this.FindControl<Button>("ProjectsButton")!.Click += (_, _) => ShowPage("projects");
        this.FindControl<Button>("CreateProjectButton")!.Click += async (_, _) => await OpenCreateProjectDialogAsync();
        this.FindControl<Button>("ViewAllProjectsButton")!.Click += (_, _) => ShowPage("projects");
        this.FindControl<Button>("BackToProjectsButton")!.Click += (_, _) => ShowPage("projects");
        this.FindControl<Button>("PlanningButton")!.Click += (_, _) => OpenPlanning();
        this.FindControl<Button>("OpenPlanningButton")!.Click += (_, _) => OpenPlanning();
        this.FindControl<Button>("OpenReportsButton")!.Click += async (_, _) => await LoadProjectReportAsync();
        this.FindControl<Button>("ExportBoqButton")!.Click += async (_, _) => await ExportProjectReportAsync("boq.csv");
        this.FindControl<Button>("ExportTransactionsButton")!.Click += async (_, _) => await ExportProjectReportAsync("transactions.csv");
        this.FindControl<Button>("CostButton")!.Click += async (_, _) => await ShowCostControlAsync();
        this.FindControl<Button>("ReportsButton")!.Click += async (_, _) => await ShowReportsAsync();
        this.FindControl<Button>("OrganizationButton")!.Click += (_, _) => ShowUtility("Organization", "Organization and branch administration is available through the secured API. The desktop administration screens are being completed.");
        this.FindControl<Button>("SettingsButton")!.Click += (_, _) => ShowUtility("Settings", "Server address is configured on the sign-in screen. Credentials and access tokens are not stored by this desktop client.");
        this.FindControl<TextBox>("ProjectSearchBox")!.TextChanged += (_, _) => RenderProjects();
    }

    private string LoadServerAddress()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return "http://127.0.0.1:5080/";
            var settings = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(_settingsPath));
            return string.IsNullOrWhiteSpace(settings?.ServerUrl) ? "http://127.0.0.1:5080/" : settings.ServerUrl;
        }
        catch { return "http://127.0.0.1:5080/"; }
    }

    private bool TryGetServerAddress(out Uri address)
    {
        address = null!;
        if (!Uri.TryCreate(this.FindControl<TextBox>("ServerUrlBox")!.Text?.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttps &&
                !(parsed.Scheme == Uri.UriSchemeHttp && (parsed.IsLoopback || parsed.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))))
        {
            this.FindControl<TextBlock>("LoginStatusText")!.Text = "Use a valid HTTPS address. HTTP is allowed only for localhost.";
            return false;
        }
        address = new Uri(parsed.ToString().TrimEnd('/') + "/");
        return true;
    }

    private async Task LoginAsync()
    {
        var status = this.FindControl<TextBlock>("LoginStatusText")!;
        var button = this.FindControl<Button>("LoginButton")!;
        if (!TryGetServerAddress(out var address)) return;
        var tenant = this.FindControl<TextBox>("TenantKeyBox")!.Text?.Trim();
        var email = this.FindControl<TextBox>("EmailBox")!.Text?.Trim();
        var password = this.FindControl<TextBox>("PasswordBox")!.Text;
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            status.Text = "Enter your tenant key, email address and password.";
            return;
        }

        button.IsEnabled = false;
        status.Text = "Signing in securely...";
        try
        {
            using var response = await _http.PostAsJsonAsync(new Uri(address, "api/v1/auth/login"),
                new { tenantKey = tenant, email, password });
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Sign-in failed. Check the tenant key, email and password.");
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"The server returned HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            var login = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions)
                ?? throw new InvalidDataException("The server returned an empty sign-in response.");

            _serverAddress = address;
            _accessToken = login.AccessToken;
            _refreshToken = login.RefreshToken;
            _tenantKey = tenant;
            SaveServerAddress(address);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            this.FindControl<TextBox>("PasswordBox")!.Text = string.Empty;
            this.FindControl<Border>("LoginPanel")!.IsVisible = false;
            this.FindControl<Grid>("WorkspacePanel")!.IsVisible = true;
            this.FindControl<TextBlock>("UserLabel")!.Text = login.Email;
            this.FindControl<TextBlock>("ServerAddressLabel")!.Text = address.ToString();
            this.FindControl<TextBlock>("ConnectionText")!.Text = "Session active";
            this.FindControl<Avalonia.Controls.Shapes.Ellipse>("ConnectionDot")!.Fill = Brush("#22C55E");
            await LoadWorkspaceAsync();
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
        }
        finally { button.IsEnabled = true; }
    }

    private void SaveServerAddress(Uri address)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new DesktopSettings(address.ToString())));
        }
        catch (Exception ex)
        {
            this.FindControl<TextBlock>("LoginStatusText")!.Text = $"Could not save server address: {ex.Message}";
        }
    }

    private async Task LoadWorkspaceAsync()
    {
        try
        {
            var summary = await GetJsonWithRefreshAsync<DashboardSummary>(new Uri(_serverAddress!, "api/v1/dashboard/summary"));
            _projects = await GetJsonWithRefreshAsync<List<ProjectItem>>(new Uri(_serverAddress!, "api/v1/projects")) ?? [];
            this.FindControl<TextBlock>("TotalProjectsValue")!.Text = (summary?.TotalProjects ?? _projects.Count).ToString();
            this.FindControl<TextBlock>("ActiveProjectsValue")!.Text = _projects.Count(p => p.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)).ToString();
            this.FindControl<TextBlock>("OnHoldProjectsValue")!.Text = _projects.Count(p => p.Status.Equals("OnHold", StringComparison.OrdinalIgnoreCase)).ToString();
            RenderRecentProjects();
            RenderProjects();
            this.FindControl<TextBlock>("DashboardStatusText")!.Text = $"Portfolio data loaded from server at {DateTimeOffset.Now:t}.";
        }
        catch (Exception ex)
        {
            this.FindControl<TextBlock>("DashboardStatusText")!.Text = $"Could not load portfolio data: {ex.Message}";
            this.FindControl<TextBlock>("ConnectionText")!.Text = "API unavailable";
            this.FindControl<Avalonia.Controls.Shapes.Ellipse>("ConnectionDot")!.Fill = Brush("#DC6B45");
        }
    }

    private void RenderRecentProjects()
    {
        var panel = this.FindControl<StackPanel>("RecentProjectsList")!;
        panel.Children.Clear();
        if (_projects.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "No projects are assigned to this account.", Foreground = Brush("#64748B") });
            return;
        }
        foreach (var project in _projects.Take(5)) panel.Children.Add(ProjectCard(project));
    }

    private void RenderProjects()
    {
        var panel = this.FindControl<StackPanel>("ProjectsList");
        if (panel is null) return;
        panel.Children.Clear();
        var filter = this.FindControl<TextBox>("ProjectSearchBox")?.Text?.Trim();
        var rows = _projects.Where(p => string.IsNullOrWhiteSpace(filter)
            || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || p.Code.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var project in rows) panel.Children.Add(ProjectCard(project));
        this.FindControl<TextBlock>("ProjectsStatusText")!.Text = $"{rows.Count} project(s) shown.";
    }

    private Control ProjectCard(ProjectItem project)
    {
        var card = new Border { Background = Brushes.White, BorderBrush = Brush("#E1E7EF"), BorderThickness = new Avalonia.Thickness(1), CornerRadius = new Avalonia.CornerRadius(10), Padding = new Avalonia.Thickness(16) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var info = new StackPanel { Spacing = 5 };
        info.Children.Add(new TextBlock { Text = project.Name, FontSize = 16, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Brush("#172B4D") });
        info.Children.Add(new TextBlock { Text = $"{project.Code}   ·   {project.Status}   ·   {project.CurrencyCode}", FontSize = 11, Foreground = Brush("#64748B") });
        grid.Children.Add(info);
        var open = new Button { Content = "Open project", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        open.Click += (_, _) => _ = OpenProjectAsync(project);
        Grid.SetColumn(open, 1); grid.Children.Add(open);
        card.Child = grid;
        return card;
    }

    private async Task OpenCreateProjectDialogAsync()
    {
        if (_serverAddress is null) return;
        var status = new TextBlock { Foreground = Brush("#64748B"), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var code = new TextBox { Watermark = "e.g. PRJ-001", MinHeight = 38 };
        var name = new TextBox { Watermark = "Project name", MinHeight = 38 };
        var currency = new TextBox { Text = "USD", Watermark = "Three-letter currency code", MinHeight = 38 };
        var timeZone = new TextBox { Text = "UTC", Watermark = "IANA time zone", MinHeight = 38 };
        var companyPicker = new ComboBox { MinHeight = 38, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        List<CompanyItem> companies;
        try
        {
            companies = await GetJsonWithRefreshAsync<List<CompanyItem>>(new Uri(_serverAddress, "api/v1/organization/companies")) ?? [];
        }
        catch (Exception ex)
        {
            this.FindControl<TextBlock>("ProjectsStatusText")!.Text = $"Could not load companies: {ex.Message}";
            return;
        }
        if (companies.Count == 0)
        {
            this.FindControl<TextBlock>("ProjectsStatusText")!.Text = "No active company is assigned to your account.";
            return;
        }
        companyPicker.ItemsSource = companies.Select(x => $"{x.Code} — {x.Name}").ToList();
        companyPicker.SelectedIndex = 0;

        var dialog = new Window
        {
            Title = "Create project",
            Width = 520,
            Height = 590,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#F4F6FA")
        };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(26), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "New project", FontSize = 25, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Brush("#172B4D") });
        panel.Children.Add(new TextBlock { Text = "Create a project in one of your active companies. Your account must have Company Admin permission.", Foreground = Brush("#64748B"), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(FormLabel("Company", companyPicker));
        panel.Children.Add(FormLabel("Project code", code));
        panel.Children.Add(FormLabel("Project name", name));
        panel.Children.Add(FormLabel("Currency", currency));
        panel.Children.Add(FormLabel("Time zone", timeZone));
        panel.Children.Add(status);
        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var create = new Button { Content = "Create project", MinWidth = 130, Background = Brush("#2563A6"), Foreground = Brushes.White };
        cancel.Click += (_, _) => dialog.Close();
        create.Click += async (_, _) =>
        {
            var selectedIndex = companyPicker.SelectedIndex;
            if (selectedIndex < 0 || string.IsNullOrWhiteSpace(code.Text) || string.IsNullOrWhiteSpace(name.Text)
                || string.IsNullOrWhiteSpace(currency.Text) || string.IsNullOrWhiteSpace(timeZone.Text))
            {
                status.Text = "Complete all fields before creating the project.";
                return;
            }
            create.IsEnabled = false;
            status.Text = "Creating project...";
            try
            {
                using var response = await _http.PostAsJsonAsync(new Uri(_serverAddress, "api/v1/projects"), new
                {
                    companyId = companies[selectedIndex].Id,
                    branchId = (Guid?)null,
                    code = code.Text.Trim(),
                    name = name.Text.Trim(),
                    currencyCode = currency.Text.Trim().ToUpperInvariant(),
                    timeZone = timeZone.Text.Trim()
                });
                if (response.StatusCode == HttpStatusCode.Forbidden)
                    throw new InvalidOperationException("Only a Company Admin can create projects.");
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Project creation failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
                dialog.Close();
                await LoadWorkspaceAsync();
                ShowPage("projects");
                this.FindControl<TextBlock>("ProjectsStatusText")!.Text = "Project created successfully.";
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                create.IsEnabled = true;
            }
        };
        actions.Children.Add(cancel);
        actions.Children.Add(create);
        panel.Children.Add(actions);
        dialog.Content = panel;
        await dialog.ShowDialog(this);
    }

    private static Control FormLabel(string label, Control control)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = label, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Brush("#334155") });
        stack.Children.Add(control);
        return stack;
    }

    private async Task OpenProjectAsync(ProjectItem project)
    {
        _selectedProject = project;
        this.FindControl<TextBlock>("DetailProjectName")!.Text = project.Name;
        this.FindControl<TextBlock>("DetailProjectMeta")!.Text = $"{project.Code}  ·  {project.Status}  ·  {project.CurrencyCode}  ·  {project.Id}";
        ShowPage("detail");
        await LoadProjectReportAsync();
    }

    private async Task LoadProjectReportAsync()
    {
        if (_selectedProject is null || _serverAddress is null) return;
        var status = this.FindControl<TextBlock>("DetailReportStatus")!;
        status.Text = "Loading live project report...";
        try
        {
            var report = await GetJsonWithRefreshAsync<ProjectOverview>(new Uri(_serverAddress, $"api/v1/projects/{_selectedProject.Id}/reports/overview"));
            if (report is null) throw new InvalidDataException("The project report was empty.");
            this.FindControl<TextBlock>("DetailActivities")!.Text = report.Schedule.TotalActivities.ToString();
            this.FindControl<TextBlock>("DetailProgress")!.Text = $"{report.Schedule.AveragePercentComplete:0.#}%";
            this.FindControl<TextBlock>("DetailCritical")!.Text = report.Schedule.CriticalOpen.ToString();
            this.FindControl<TextBlock>("DetailScheduleStatus")!.Text =
                $"Schedule: {report.Schedule.LastCalculatedAt?.ToLocalTime().ToString("g") ?? "Not calculated"}  ·  Delayed activities: {report.Schedule.Delayed}";
            status.Text = report.Cost is null
                ? "Cost figures are hidden because this account does not have ReadCost permission."
                : $"Cost overview available in {report.Cost.Currency}. Approved budget: {report.Cost.BudgetAtCompletion:N2}. Actual cost: {report.Cost.ActualCost:N2}.";
        }
        catch (Exception ex) { status.Text = $"Could not load project report: {ex.Message}"; }
    }

    private async Task ExportProjectReportAsync(string reportName)
    {
        if (_selectedProject is null || _serverAddress is null) return;
        var status = this.FindControl<TextBlock>("DetailReportStatus")!;
        status.Text = "Preparing " + reportName + "...";
        try
        {
            var uri = new Uri(_serverAddress, "api/v1/projects/" + _selectedProject.Id + "/reports/" + reportName);
            using var response = await SendWithRefreshAsync(() => _http.GetAsync(uri));
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new InvalidOperationException("Your account does not have permission to export this report.");
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("Report export failed (" + (int)response.StatusCode + "): " + await response.Content.ReadAsStringAsync());
            var bytes = await response.Content.ReadAsByteArrayAsync();
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PROJEX", "Exports");
            Directory.CreateDirectory(folder);
            var fileName = _selectedProject.Code + "-" + Path.GetFileNameWithoutExtension(reportName) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv";
            var fullPath = Path.Combine(folder, fileName);
            await File.WriteAllBytesAsync(fullPath, bytes);
            status.Text = "Export saved: " + fullPath;
        }
        catch (Exception ex)
        {
            status.Text = "Could not export report: " + ex.Message;
        }
    }

    private void OpenPlanning()
    {
        if (_selectedProject is null || _serverAddress is null || string.IsNullOrWhiteSpace(_accessToken))
        {
            ShowPage("projects");
            this.FindControl<TextBlock>("ProjectsStatusText")!.Text = "Open a project first to access its live planning schedule.";
            return;
        }
        new GanttConnectionWindow(_serverAddress.ToString(), _accessToken, _selectedProject.Id, RefreshAccessTokenForPlanningAsync).Show(this);
    }

    private void ShowPage(string page)
    {
        this.FindControl<StackPanel>("DashboardPage")!.IsVisible = page == "dashboard";
        this.FindControl<StackPanel>("ProjectsPage")!.IsVisible = page == "projects";
        this.FindControl<StackPanel>("DetailPage")!.IsVisible = page == "detail";
        this.FindControl<StackPanel>("UtilityPage")!.IsVisible = page == "utility";
    }

    private void ShowUtility(string title, string description)
    {
        this.FindControl<TextBlock>("UtilityTitle")!.Text = title;
        this.FindControl<TextBlock>("UtilityDescription")!.Text = description;
        this.FindControl<StackPanel>("CostMetricsPanel")!.IsVisible = false;
        ShowPage("utility");
    }

    private async Task ShowReportsAsync()
    {
        if (_selectedProject is null)
        {
            ShowPage("projects");
            this.FindControl<TextBlock>("ProjectsStatusText")!.Text = "Select a project to view its live report and export data.";
            return;
        }
        ShowPage("detail");
        await LoadProjectReportAsync();
    }

    private async Task ShowCostControlAsync()
    {
        if (_selectedProject is null || _serverAddress is null)
        {
            ShowPage("projects");
            this.FindControl<TextBlock>("ProjectsStatusText")!.Text = "Open a project first to view its cost position.";
            return;
        }
        this.FindControl<TextBlock>("UtilityTitle")!.Text = "Cost control — " + _selectedProject.Name;
        this.FindControl<TextBlock>("UtilityDescription")!.Text = "Live approved budget, actual expenditure and commitments from the project costing module.";
        this.FindControl<StackPanel>("CostMetricsPanel")!.IsVisible = true;
        ShowPage("utility");
        this.FindControl<TextBlock>("CostBudgetValue")!.Text = "Loading...";
        try
        {
            var cost = await GetJsonWithRefreshAsync<CostSummary>(
                new Uri(_serverAddress, "api/v1/projects/" + _selectedProject.Id + "/costing/summary"));
            if (cost is null) throw new InvalidDataException("The costing API returned an empty response.");
            string Format(decimal value) => cost.Currency + " " + value.ToString("N2");
            this.FindControl<TextBlock>("CostBudgetValue")!.Text = Format(cost.BudgetAtCompletion);
            this.FindControl<TextBlock>("CostActualValue")!.Text = Format(cost.ActualCost);
            this.FindControl<TextBlock>("CostCommittedValue")!.Text = Format(cost.CommittedCost);
            this.FindControl<TextBlock>("CostVarianceValue")!.Text =
                "Budget less actual: " + Format(cost.BudgetMinusActual) + "   ·   Budget less actual and commitments: " + Format(cost.BudgetMinusActualAndCommitted);
            this.FindControl<TextBlock>("CostPendingValue")!.Text = "Transactions awaiting approval: " + cost.TransactionsAwaitingApproval;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            this.FindControl<StackPanel>("CostMetricsPanel")!.IsVisible = false;
            this.FindControl<TextBlock>("UtilityDescription")!.Text = "Your account does not have ReadCost permission for this project. Financial figures are intentionally not shown.";
        }
        catch (Exception ex)
        {
            this.FindControl<StackPanel>("CostMetricsPanel")!.IsVisible = false;
            this.FindControl<TextBlock>("UtilityDescription")!.Text = "Could not load cost data: " + ex.Message;
        }
    }

    private async Task<string> RefreshAccessTokenForPlanningAsync()
    {
        await RefreshSessionAsync();
        return _accessToken ?? throw new InvalidOperationException("Your session has expired. Sign in again.");
    }

    private async Task<T?> GetJsonWithRefreshAsync<T>(Uri uri)
    {
        using var response = await SendWithRefreshAsync(() => _http.GetAsync(uri));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    private async Task<HttpResponseMessage> SendWithRefreshAsync(Func<Task<HttpResponseMessage>> send)
    {
        var response = await send();
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
        response.Dispose();
        await RefreshSessionAsync();
        return await send();
    }

    private async Task RefreshSessionAsync()
    {
        if (_serverAddress is null || string.IsNullOrWhiteSpace(_tenantKey) || string.IsNullOrWhiteSpace(_refreshToken))
            throw new InvalidOperationException("Your session has expired. Sign in again.");
        using var response = await _http.PostAsJsonAsync(new Uri(_serverAddress, "api/v1/auth/refresh"),
            new { tenantKey = _tenantKey, refreshToken = _refreshToken });
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Your session expired or was revoked. Sign in again.");
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions)
            ?? throw new InvalidDataException("The server returned an empty refresh response.");
        _accessToken = tokens.AccessToken;
        _refreshToken = tokens.RefreshToken;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    private async Task LogoutAsync()
    {
        if (_serverAddress is not null && !string.IsNullOrWhiteSpace(_accessToken))
        {
            try
            {
                using var response = await _http.PostAsync(new Uri(_serverAddress, "api/v1/auth/logout"), null);
            }
            catch { /* Best-effort server-side revocation; never block local sign-out. */ }
        }
        _accessToken = null; _refreshToken = null; _tenantKey = null; _serverAddress = null; _selectedProject = null; _projects.Clear();
        _http.DefaultRequestHeaders.Authorization = null;
        this.FindControl<Grid>("WorkspacePanel")!.IsVisible = false;
        this.FindControl<Border>("LoginPanel")!.IsVisible = true;
        this.FindControl<TextBlock>("LoginStatusText")!.Text = "You have signed out.";
        ShowPage("dashboard");
    }

    private static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));
    private sealed record DesktopSettings(string ServerUrl);
    private sealed class LoginResponse
    {
        public string AccessToken { get; set; } = "";
        public string? RefreshToken { get; set; }
        public string Email { get; set; } = "";
    }
    private sealed class TokenResponse
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
    }
    private sealed class DashboardSummary { public int TotalProjects { get; set; } }
    private sealed class CompanyItem
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
    }
    private sealed class ProjectItem
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string CurrencyCode { get; set; } = "";
        public string Status { get; set; } = "";
    }
    private sealed class ProjectOverview
    {
        public ScheduleSummary Schedule { get; set; } = new();
        public CostSummary? Cost { get; set; }
    }
    private sealed class ScheduleSummary
    {
        public int TotalActivities { get; set; }
        public int CriticalOpen { get; set; }
        public int Delayed { get; set; }
        public decimal AveragePercentComplete { get; set; }
        public DateTimeOffset? LastCalculatedAt { get; set; }
    }
    private sealed class CostSummary
    {
        public string Currency { get; set; } = "";
        public decimal BudgetAtCompletion { get; set; }
        public decimal ActualCost { get; set; }
        public decimal CommittedCost { get; set; }
        public decimal AccruedCost { get; set; }
        public decimal BudgetMinusActual { get; set; }
        public decimal BudgetMinusActualAndCommitted { get; set; }
        public int TransactionsAwaitingApproval { get; set; }
    }
}