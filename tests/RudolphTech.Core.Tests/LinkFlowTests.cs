using RudolphTech.Core.Settings;

namespace RudolphTech.Core.Tests;

/// <summary>
/// There is only one password (the web one), asked once in the prompt that opens the settings
/// window; everything else that needs the web reuses it from memory. These are the pure decisions
/// behind that flow: what the prompt says, whether opening the window should link right away, and
/// whether the web actions inside it can run.
/// </summary>
public class LinkFlowTests
{
    [Fact]
    public void AsksToLinkWhenNotYetLinked()
    {
        Assert.Equal("Ingresá la contraseña de la aplicación para vincular esta PC.", LinkFlow.PromptText(linked: false));
    }

    [Fact]
    public void AsksToSeeTheConfigurationWhenAlreadyLinked()
    {
        Assert.Equal("Ingresá la contraseña de la aplicación para ver la configuración.", LinkFlow.PromptText(linked: true));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void OnlyLinksOnOpenWhenNotLinkedAndAPasswordIsHeld(bool linked, bool hasPassword, bool expected)
    {
        var inputs = new LinkFlowInputs { Linked = linked, HasPasswordInMemory = hasPassword };

        Assert.Equal(expected, LinkFlow.ShouldLinkOnOpen(inputs));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void WebActionsNeedAPasswordInMemoryRegardlessOfWhetherAlreadyLinked(bool hasPassword, bool expected)
    {
        Assert.True(LinkFlow.WebActionsEnabled(new LinkFlowInputs { Linked = true, HasPasswordInMemory = hasPassword }) == expected);
        Assert.True(LinkFlow.WebActionsEnabled(new LinkFlowInputs { Linked = false, HasPasswordInMemory = hasPassword }) == expected);
    }

    [Fact]
    public void AWrongPasswordNeverGetsFarEnoughToConsiderLinkingOnOpen()
    {
        // The prompt itself is what rejects a wrong password (it never returns OK), so the window
        // never opens and this decision is never even asked for that case. Documented here as the
        // contract: without a password in memory (the only way a rejected prompt could reach this),
        // linking on open never happens, whatever the linked state is.
        Assert.False(LinkFlow.ShouldLinkOnOpen(new LinkFlowInputs { Linked = false, HasPasswordInMemory = false }));
        Assert.False(LinkFlow.ShouldLinkOnOpen(new LinkFlowInputs { Linked = true, HasPasswordInMemory = false }));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ActualizarAgenteAndVolverAVincularOnlyShowOnceLinked(bool linked, bool expected)
    {
        // Regardless of whether a password happens to be in memory: there is nothing to update and
        // nothing to link "again" before the first link ever happened.
        Assert.Equal(expected, LinkFlow.WebActionsVisible(new LinkFlowInputs { Linked = linked, HasPasswordInMemory = true }));
        Assert.Equal(expected, LinkFlow.WebActionsVisible(new LinkFlowInputs { Linked = linked, HasPasswordInMemory = false }));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TheQuietChangeAddressLinkIsTheMirrorOfWebActionsVisible(bool linked, bool expected)
    {
        Assert.Equal(expected, LinkFlow.ChangeAddressVisible(new LinkFlowInputs { Linked = linked, HasPasswordInMemory = true }));
    }
}
