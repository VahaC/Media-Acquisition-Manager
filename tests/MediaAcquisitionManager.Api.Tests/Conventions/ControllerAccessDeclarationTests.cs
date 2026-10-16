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
        Assert.Contains("AuthController.ChangePassword", actions);
        Assert.Contains("SetupController.Complete", actions);
        Assert.Contains("UsersController.Delete", actions);
        // MAM-5 — the owner's revoke of all of a user's personal access tokens, owner-only with the rest of Users.
        Assert.Contains("UsersController.RevokeTokens", actions);
        Assert.Contains("IntegrationsController.Create", actions);
        Assert.Contains("IntegrationsController.TestDraft", actions);
        Assert.Contains("LibraryController.Works", actions);
        // MAM-35 — a GET outside Controllers/Library/Links: every signed-in user's, like the works it pictures.
        Assert.Contains("LibraryController.Poster", actions);
        // MAM-20 — a GET outside Controllers/Library/Links: every signed-in user's, like the works it groups.
        Assert.Contains("LibraryDuplicatesController.List", actions);
        // MAM-21 — the owner's Confirm, Dismiss and Undo of a possible match, beside the list: [Authorize] by the
        // controller, owner-only by their own [RequireInstallationOwner] (LibraryWritesAreOwnerOnlyHttpTests).
        Assert.Contains("LibraryDuplicatesController.Decide", actions);
        Assert.Contains("LibraryDuplicatesController.Undo", actions);
        Assert.Contains("LibrarySyncController.Start", actions);
        Assert.Contains("LibrarySyncController.GetRun", actions);
        Assert.Contains("AllowedRootsController.Get", actions);
        Assert.Contains("AllowedRootsController.Update", actions);
        Assert.Contains("FileInspectionController.Start", actions);
        Assert.Contains("FileInspectionController.GetRun", actions);
        Assert.Contains("FolderScanController.Start", actions);
        Assert.Contains("FolderScanController.GetRun", actions);
        Assert.Contains("ReleaseSearchController.Search", actions);
        Assert.Contains("SearchesController.Create", actions);
        Assert.Contains("SearchesController.Get", actions);
        Assert.Contains("SearchesController.Releases", actions);
        Assert.Contains("DestinationsController.Create", actions);
        Assert.Contains("DestinationsController.ClientDestinations", actions);
        Assert.Contains("DownloadsController.Options", actions);
        Assert.Contains("DownloadsController.OverwriteCheck", actions);
        Assert.Contains("DownloadsController.Create", actions);
        Assert.Contains("DownloadsController.Get", actions);
        Assert.Contains("DownloadsController.List", actions);
        Assert.Contains("DownloadsController.Files", actions);
        Assert.Contains("DownloadsController.Cancel", actions);
        // MAM-33 — outside Controllers/Settings and Controllers/Library: every signed-in user's, the owner's write
        // declared on its action.
        Assert.Contains("RecommendationsController.LatestRun", actions);
        Assert.Contains("RecommendationsController.StartRun", actions);
        Assert.Contains("RecommendationsController.List", actions);
        Assert.Contains("RecommendationsController.Dismiss", actions);
        Assert.Contains("RecommendationsController.Undismiss", actions);
        Assert.Contains("RecommendationsController.Poster", actions);
        // The signed-in user's own preferences, by the token: outside Controllers/Settings, every user's.
        Assert.Contains("ProfileController.GetRecommendationSettings", actions);
        Assert.Contains("ProfileController.UpdateRecommendationSettings", actions);
        // MAM-5 — the signed-in user's own personal access tokens, beside the Profile's settings; refused to tokens.
        Assert.Contains("PersonalAccessTokensController.List", actions);
        Assert.Contains("PersonalAccessTokensController.Create", actions);
        Assert.Contains("PersonalAccessTokensController.Revoke", actions);
        // MAM-34 — under Controllers/Library/Links: the owner's alone, every action, by the namespace's convention (R19).
        Assert.Contains("LibraryLinksController.Dialog", actions);
        Assert.Contains("LibraryLinksController.Set", actions);
        Assert.Contains("LibraryLinksController.Remove", actions);
        Assert.Contains("LibraryLinksController.Write", actions);
        Assert.Contains("LibraryLinkListsController.List", actions);
        Assert.Contains("LibraryLinkListsController.WriteLink", actions);
        Assert.Contains("LibraryLinkListsController.RemoveLink", actions);
        Assert.Contains("LibraryLinkListsController.Batch", actions);
        Assert.Contains("LibraryLinkSuggestionsController.Search", actions);
        Assert.Contains("LibraryLinkSuggestionsController.Lookup", actions);
        Assert.Contains("LibraryLinkSuggestionsController.MarkNoneFits", actions);
        Assert.Contains("LibraryLinkSuggestionsController.ClearNoneFits", actions);
        Assert.Contains("LibraryLinkReviewController.Queue", actions);
        Assert.Contains("LibraryLinkReviewController.Confident", actions);
        Assert.Contains("LibraryLinkReviewController.AcceptConfident", actions);
        Assert.Contains("LibraryLinkReviewController.LatestRun", actions);
        Assert.Contains("LibraryLinkReviewController.StartRun", actions);
        Assert.Contains("LibraryLinkCatchUpController.Read", actions);
        Assert.Contains("LibraryLinkCatchUpController.Start", actions);
        // MAM-24 — under Controllers/Replacements: the owner's alone, every action, by the namespace's convention (ADR 0033).
        Assert.Contains("ReplacementsController.Options", actions);
        Assert.Contains("ReplacementsController.Schedule", actions);
        // MAM-26 — the owner's details, Retry, Restore and Delete for good of a replacement, in the same namespace.
        Assert.Contains("ReplacementJobsController.Get", actions);
        Assert.Contains("ReplacementJobsController.Retry", actions);
        Assert.Contains("ReplacementJobsController.Restore", actions);
        Assert.Contains("ReplacementJobsController.DeleteForGood", actions);
        // MAM-27 — the owner's Keep both and Drop of one ticked copy, in the same namespace.
        Assert.Contains("ReplacementJobsController.Cancel", actions);
        Assert.Contains("ReplacementJobsController.DropCopy", actions);
        // MAM-40 — the owner's removals of a duplicate copy, their Restore and Delete for good, in the same namespace.
        Assert.Contains("CopyRemovalsController.List", actions);
        Assert.Contains("CopyRemovalsController.Remove", actions);
        Assert.Contains("CopyRemovalsController.Restore", actions);
        Assert.Contains("CopyRemovalsController.DeleteForGood", actions);
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
