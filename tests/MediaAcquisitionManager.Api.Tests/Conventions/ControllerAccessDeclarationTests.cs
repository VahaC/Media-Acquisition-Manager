using MediaAcquisitionManager.TestInfrastructure.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;

namespace MediaAcquisitionManager.Api.Tests.Conventions;

// MAM-4 — every controller action says who may call it: [Authorize] or [AllowAnonymous], on the
// action or on its controller. An action with neither is anonymous by omission — a decision nobody
// made. Read from the running app's endpoints, so it sees exactly what the authorization middleware
// sees.
public sealed class ControllerAccessDeclarationTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private readonly MediaAcquisitionManagerApiFactory _factory;

    public ControllerAccessDeclarationTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync() => await _factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Every_controller_action_of_the_api_declares_authorize_or_allow_anonymous()
    {
        //Arrange
        var endpoints = ApiControllerEndpoints();

        //Act
        var undeclared = endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
                && !endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any())
            .Select(Describe)
            .ToList();

        //Assert
        Assert.Empty(undeclared);
    }

    [Fact]
    public void The_scan_covers_the_controllers_of_the_api()
    {
        //Arrange

        //Act
        var actions = ApiControllerEndpoints().Select(Describe).ToList();

        //Assert
        // Guards the guard: an empty or partial scan would let the test above pass for nothing.
        Assert.True(actions.Count >= 10, $"Only {actions.Count} controller actions were found.");
        Assert.Contains("AuthController.SignIn", actions);
        Assert.Contains("AuthController.Me", actions);
        Assert.Contains("SetupController.Complete", actions);
        Assert.Contains("UsersController.Delete", actions);
        Assert.Contains("IntegrationsController.Create", actions);
        Assert.Contains("IntegrationsController.TestDraft", actions);
        Assert.Contains("LibraryController.Works", actions);
        Assert.Contains("LibrarySyncController.Start", actions);
        Assert.Contains("LibrarySyncController.GetRun", actions);
        Assert.Contains("AllowedRootsController.Get", actions);
        Assert.Contains("AllowedRootsController.Update", actions);
        Assert.Contains("FileInspectionController.Start", actions);
        Assert.Contains("FileInspectionController.GetRun", actions);
        Assert.Contains("ReleaseSearchController.Search", actions);
        Assert.Contains("SearchesController.Create", actions);
        Assert.Contains("SearchesController.Get", actions);
        Assert.Contains("SearchesController.Releases", actions);
        Assert.Contains("DestinationsController.Create", actions);
        Assert.Contains("DestinationsController.ClientDestinations", actions);
        Assert.Contains("DownloadsController.Options", actions);
        Assert.Contains("DownloadsController.Create", actions);
        Assert.Contains("DownloadsController.Get", actions);
        Assert.Contains("DownloadsController.List", actions);
        Assert.Contains("DownloadsController.Files", actions);
        Assert.Contains("DownloadsController.Cancel", actions);
        Assert.Contains("UnknownApiController.Unmatched", actions);
    }

    // The Api assembly's own actions only: test-only probe controllers aren't the app's surface.
    private List<Endpoint> ApiControllerEndpoints() =>
        _factory.Resolve<EndpointDataSource>().Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Assembly == typeof(Program).Assembly)
            .ToList();

    private static string Describe(Endpoint endpoint)
    {
        var action = endpoint.Metadata.GetRequiredMetadata<ControllerActionDescriptor>();
        return $"{action.ControllerTypeInfo.Name}.{action.ActionName}";
    }
}
