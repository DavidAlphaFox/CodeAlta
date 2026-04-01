using CodeAlta.Frontend.Commands;
using XenoAtom.Terminal.UI;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ShellCommandSurfaceCoordinatorTests
{
    [TestMethod]
    public void CommandPalettePopupStyle_IsBottomCenteredHalfWidth()
    {
        var style = ShellCommandSurfaceCoordinator.CommandPalettePopupStyle;

        Assert.AreEqual(50d, style.PopupWidthPercent.GetValueOrDefault());
        Assert.AreEqual(int.MaxValue, style.MaxWidth);
        Assert.AreEqual(Align.Center, style.PopupHorizontalAlignment);
        Assert.AreEqual(Align.End, style.PopupVerticalAlignment);
        Assert.AreEqual(-2, style.PopupOffsetY);
    }
}
