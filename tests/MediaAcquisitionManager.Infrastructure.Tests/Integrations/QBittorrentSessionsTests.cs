using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-11 — the sessions the qBittorrent adapter keeps between calls: one per saved integration, used only
// with the address and credentials it was opened with, never for a draft, dropped only while it is still the
// one kept, and never printed. The sign-ins qBittorrent refused are kept alike, and forgotten once the same
// credentials sign in or other ones do.
public sealed class QBittorrentSessionsTests
{
    private const string SessionId = "Zq3xY9LmN2pQ7rT4vW8sK1aB6cD0eF5g";

    private static readonly IntegrationConnectionSettings Saved =
        new(Guid.Parse("7c1e5a3b-2d4f-4a6b-8c9d-0e1f2a3b4c5d"), "http://qbittorrent.test:8080", "admin", "correct-horse-battery", null);

    private readonly QBittorrentSessions _sessions = new();

    /// <summary>The kept session's settings with one thing changed each.</summary>
    public static TheoryData<string> Edits => ["address", "user name", "password", "password removed"];

    [Fact]
    public void Find_returns_the_session_kept_for_the_same_integration_address_and_credentials()
    {
        //Arrange
        var session = Session(Saved);
        _sessions.Keep(Saved, session);

        //Act
        var found = _sessions.Find(Saved with { });

        //Assert
        Assert.Same(session, found);
    }

    [Theory]
    [MemberData(nameof(Edits))]
    public void Find_once_the_address_or_credentials_have_changed_finds_nothing(string edit)
    {
        //Arrange
        _sessions.Keep(Saved, Session(Saved));
        var edited = edit switch
        {
            "address" => Saved with { BaseUrl = "http://qbittorrent.test:8081" },
            "user name" => Saved with { UserName = "Admin" },
            "password" => Saved with { Password = "correct-horse-battery-staple" },
            _ => Saved with { Password = null },
        };

        //Act
        var found = _sessions.Find(edited);

        //Assert
        Assert.Null(found);
    }

    [Fact]
    public void Find_tells_apart_credentials_that_run_together_into_the_same_text()
    {
        //Arrange
        var kept = Saved with { UserName = "admin1", Password = "23456789" };
        _sessions.Keep(kept, Session(kept));

        //Act
        var found = _sessions.Find(Saved with { UserName = "admin", Password = "123456789" });

        //Assert
        Assert.Null(found);
    }

    [Fact]
    public void Find_takes_a_missing_password_as_the_empty_one_it_signs_in_with()
    {
        //Arrange
        var withoutPassword = Saved with { Password = null };
        var session = Session(withoutPassword);
        _sessions.Keep(withoutPassword, session);

        //Act
        var found = _sessions.Find(Saved with { Password = "" });

        //Assert
        Assert.Same(session, found);
    }

    [Fact]
    public void Find_for_another_integration_finds_nothing()
    {
        //Arrange
        _sessions.Keep(Saved, Session(Saved));

        //Act
        var found = _sessions.Find(Saved with { ConnectionId = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d") });

        //Assert
        Assert.Null(found);
    }

    [Fact]
    public void Keep_of_an_unsaved_draft_keeps_nothing()
    {
        //Arrange
        var draft = Saved with { ConnectionId = null };

        //Act
        _sessions.Keep(draft, Session(draft));

        //Assert
        Assert.Null(_sessions.Find(draft));
    }

    [Fact]
    public void Keep_replaces_the_session_kept_before()
    {
        //Arrange
        _sessions.Keep(Saved, Session(Saved));
        var renewed = Session(Saved, "Bb7xY9LmN2pQ7rT4vW8sK1aB6cD0eF5h");

        //Act
        _sessions.Keep(Saved, renewed);

        //Assert
        Assert.Same(renewed, _sessions.Find(Saved));
    }

    [Fact]
    public void Drop_forgets_the_kept_session()
    {
        //Arrange
        var session = Session(Saved);
        _sessions.Keep(Saved, session);

        //Act
        _sessions.Drop(Saved, session);

        //Assert
        Assert.Null(_sessions.Find(Saved));
    }

    [Fact]
    public void Drop_of_a_session_no_longer_kept_leaves_the_one_kept_now()
    {
        //Arrange
        // Two calls found the same session forgotten; the first has already signed in again.
        var forgotten = Session(Saved);
        var renewed = Session(Saved, SessionId);
        _sessions.Keep(Saved, renewed);

        //Act
        _sessions.Drop(Saved, forgotten);

        //Assert
        // Same cookie, another session: only the very one dropped is forgotten.
        Assert.Same(renewed, _sessions.Find(Saved));
    }

    [Theory]
    [InlineData(IntegrationCallStatus.AuthenticationFailed)]
    [InlineData(IntegrationCallStatus.Refused)]
    public void FindRefusal_returns_how_qbittorrent_refused_the_same_integration_address_and_credentials(IntegrationCallStatus status)
    {
        //Arrange
        _sessions.KeepRefusal(Saved, status);

        //Act
        var found = _sessions.FindRefusal(Saved with { });

        //Assert
        Assert.Equal(status, found);
    }

    [Theory]
    [MemberData(nameof(Edits))]
    public void FindRefusal_once_the_address_or_credentials_have_changed_finds_nothing(string edit)
    {
        //Arrange
        _sessions.KeepRefusal(Saved, IntegrationCallStatus.AuthenticationFailed);
        var edited = edit switch
        {
            "address" => Saved with { BaseUrl = "http://qbittorrent.test:8081" },
            "user name" => Saved with { UserName = "Admin" },
            "password" => Saved with { Password = "correct-horse-battery-staple" },
            _ => Saved with { Password = null },
        };

        //Act
        var found = _sessions.FindRefusal(edited);

        //Assert
        Assert.Null(found);
    }

    [Fact]
    public void FindRefusal_for_another_integration_finds_nothing()
    {
        //Arrange
        _sessions.KeepRefusal(Saved, IntegrationCallStatus.AuthenticationFailed);

        //Act
        var found = _sessions.FindRefusal(Saved with { ConnectionId = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d") });

        //Assert
        Assert.Null(found);
    }

    [Fact]
    public void KeepRefusal_of_an_unsaved_draft_keeps_nothing()
    {
        //Arrange
        var draft = Saved with { ConnectionId = null };

        //Act
        _sessions.KeepRefusal(draft, IntegrationCallStatus.AuthenticationFailed);

        //Assert
        Assert.Null(_sessions.FindRefusal(draft));
    }

    [Fact]
    public void Keep_of_a_session_forgets_the_refusal_kept_for_the_integration()
    {
        //Arrange
        // Refused with the old password; signed in with the corrected one.
        var refused = Saved with { Password = "old-password" };
        _sessions.KeepRefusal(refused, IntegrationCallStatus.AuthenticationFailed);

        //Act
        _sessions.Keep(Saved, Session(Saved));

        //Assert
        // Going back to the old password tries it again: qBittorrent may take it by now.
        Assert.Null(_sessions.FindRefusal(refused));
    }

    [Fact]
    public void ForgetRefusal_with_the_refused_address_and_credentials_forgets_the_refusal()
    {
        //Arrange
        _sessions.KeepRefusal(Saved, IntegrationCallStatus.Refused);

        //Act
        _sessions.ForgetRefusal(Saved with { });

        //Assert
        Assert.Null(_sessions.FindRefusal(Saved));
    }

    [Fact]
    public void ForgetRefusal_with_other_credentials_leaves_the_refusal_kept()
    {
        //Arrange
        _sessions.KeepRefusal(Saved, IntegrationCallStatus.AuthenticationFailed);

        //Act
        // A test of a password typed into the Edit dialog, not saved.
        _sessions.ForgetRefusal(Saved with { Password = "correct-horse-battery-staple" });

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, _sessions.FindRefusal(Saved));
    }

    [Fact]
    public void KeepTestRefusal_without_a_kept_refusal_keeps_the_tests()
    {
        //Arrange

        //Act
        _sessions.KeepTestRefusal(Saved, IntegrationCallStatus.AuthenticationFailed);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, _sessions.FindRefusal(Saved with { }));
    }

    [Fact]
    public void KeepTestRefusal_of_the_refused_address_and_credentials_replaces_how_they_were_refused()
    {
        //Arrange
        // Banned before; the ban has ended, and the password is still wrong.
        _sessions.KeepRefusal(Saved, IntegrationCallStatus.Refused);

        //Act
        _sessions.KeepTestRefusal(Saved with { }, IntegrationCallStatus.AuthenticationFailed);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, _sessions.FindRefusal(Saved));
    }

    [Fact]
    public void KeepTestRefusal_of_other_credentials_leaves_the_kept_refusal_alone()
    {
        //Arrange
        _sessions.KeepRefusal(Saved, IntegrationCallStatus.AuthenticationFailed);
        // A test of a password typed into the Edit dialog, not saved, and refused too.
        var typed = Saved with { Password = "correct-horse-battery-staple" };

        //Act
        _sessions.KeepTestRefusal(typed, IntegrationCallStatus.AuthenticationFailed);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, _sessions.FindRefusal(Saved));
        Assert.Null(_sessions.FindRefusal(typed));
    }

    [Fact]
    public void KeepTestRefusal_of_an_unsaved_draft_keeps_nothing()
    {
        //Arrange
        var draft = Saved with { ConnectionId = null };

        //Act
        _sessions.KeepTestRefusal(draft, IntegrationCallStatus.AuthenticationFailed);

        //Assert
        Assert.Null(_sessions.FindRefusal(draft));
        Assert.Null(_sessions.FindRefusal(Saved));
    }

    [Fact]
    public void ToString_of_a_session_shows_neither_its_cookie_nor_its_value()
    {
        //Arrange
        var session = Session(Saved);

        //Act
        var text = session.ToString();

        //Assert
        Assert.Equal("QBittorrentSession { Cookie = *** }", text);
        Assert.DoesNotContain(SessionId, $"{session}", StringComparison.Ordinal);
    }

    private static QBittorrentSession Session(IntegrationConnectionSettings settings, string value = SessionId) =>
        QBittorrentSession.OpenedWith(settings, $"QBT_SID_8080={value}", value);
}
