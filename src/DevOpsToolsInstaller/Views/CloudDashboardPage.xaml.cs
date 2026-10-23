using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

public sealed partial class CloudDashboardPage : Page, IRefreshablePage
{
    private CloudDashboardState? _currentState;
    private List<KubeContextInfo> _allContexts = new();

    public CloudDashboardPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadStateAsync();
    }

    public async void RefreshView()
    {
        await LoadStateAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadStateAsync();
    }

    private async Task LoadStateAsync()
    {
        if (RefreshButton != null) RefreshButton.IsEnabled = false;

        try
        {
            var state = await CloudConnectionsService.ScanAllAsync();
            _currentState = state;

            // 1. Kubernetes
            if (state.Kube.HasKubeConfig)
            {
                KubeStatusSubtitle.Text = $"{state.Kube.Contexts.Count} context(s) configured in ~/.kube/config";
                KubeActiveBadgeText.Text = string.IsNullOrWhiteSpace(state.Kube.ActiveContextName)
                    ? "No Active Context"
                    : state.Kube.ActiveContextName;

                KubeActiveContextText.Text = string.IsNullOrWhiteSpace(state.Kube.ActiveContextName) ? "None" : state.Kube.ActiveContextName;
                KubeClusterText.Text = string.IsNullOrWhiteSpace(state.Kube.ActiveCluster) ? "—" : state.Kube.ActiveCluster;
                KubeNamespaceText.Text = string.IsNullOrWhiteSpace(state.Kube.ActiveNamespace) ? "default" : state.Kube.ActiveNamespace;

                _allContexts = state.Kube.Contexts;
                FilterContexts(KubeSearchBox?.Text);
            }
            else
            {
                KubeStatusSubtitle.Text = "No kubeconfig found at ~/.kube/config. Install kubectl or configure your cluster.";
                KubeActiveBadgeText.Text = "Not configured";
                KubeActiveContextText.Text = "None";
                KubeClusterText.Text = "—";
                KubeNamespaceText.Text = "—";
                KubeContextsList.ItemsSource = null;
            }

            // 2. AWS
            AwsStatusBadge.Text = state.Aws.StatusBadge;
            AwsProfileText.Text = state.Aws.ActiveProfile;
            AwsRegionText.Text = string.IsNullOrWhiteSpace(state.Aws.Region) ? "Not set" : state.Aws.Region;
            AwsAccountSummaryText.Text = string.IsNullOrWhiteSpace(state.Aws.AccountSummary) ? "No active keys" : state.Aws.AccountSummary;

            // 3. Azure
            AzureStatusBadge.Text = state.Azure.StatusBadge;
            AzureAccountText.Text = string.IsNullOrWhiteSpace(state.Azure.UserAccount) ? "Not logged in" : state.Azure.UserAccount;
            AzureSubscriptionText.Text = string.IsNullOrWhiteSpace(state.Azure.SubscriptionName) ? "None" : state.Azure.SubscriptionName;
            AzureEnvText.Text = state.Azure.EnvironmentName;

            // 4. GCP
            GcpStatusBadge.Text = state.Gcp.StatusBadge;
            GcpAccountText.Text = string.IsNullOrWhiteSpace(state.Gcp.AccountEmail) ? "Not logged in" : state.Gcp.AccountEmail;
            GcpProjectText.Text = string.IsNullOrWhiteSpace(state.Gcp.ProjectId) ? "No active project" : state.Gcp.ProjectId;
            GcpZoneText.Text = string.IsNullOrWhiteSpace(state.Gcp.DefaultZone) ? (string.IsNullOrWhiteSpace(state.Gcp.DefaultRegion) ? "Not set" : state.Gcp.DefaultRegion) : state.Gcp.DefaultZone;
        }
        catch (Exception ex)
        {
            ActivityLogService.Warn("Cloud Dashboard", $"Failed to load cloud state: {ex.Message}");
        }
        finally
        {
            if (RefreshButton != null) RefreshButton.IsEnabled = true;
        }
    }

    private void KubeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterContexts(KubeSearchBox.Text);
    }

    private void FilterContexts(string? query)
    {
        KubeContextsList.ItemsSource = null;
        if (string.IsNullOrWhiteSpace(query))
        {
            KubeContextsList.ItemsSource = _allContexts.ToList();
        }
        else
        {
            var q = query.Trim();
            KubeContextsList.ItemsSource = _allContexts
                .Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            c.Cluster.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            c.Namespace.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private async void SwitchContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string contextName)
        {
            bool ok = CloudConnectionsService.SwitchKubeContext(contextName);
            if (ok)
            {
                await LoadStateAsync();
            }
        }
    }

    private void AwsLogin_Click(object sender, RoutedEventArgs e)
    {
        CloudConnectionsService.LaunchLogin("aws", _currentState?.Aws.ActiveProfile);
    }

    private void AzureLogin_Click(object sender, RoutedEventArgs e)
    {
        CloudConnectionsService.LaunchLogin("azure");
    }

    private void GcpLogin_Click(object sender, RoutedEventArgs e)
    {
        CloudConnectionsService.LaunchLogin("gcp");
    }
}
