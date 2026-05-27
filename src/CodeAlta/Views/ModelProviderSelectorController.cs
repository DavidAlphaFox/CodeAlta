namespace CodeAlta.Views;

internal sealed record ModelProviderSelectorController(
    Action<int> SelectProvider,
    Action<int> SelectModel,
    Action<int> SelectReasoning,
    Action CompactSession)
{
    public static ModelProviderSelectorController Create(
        Action<int> selectProvider,
        Action<int> selectModel,
        Action<int> selectReasoning,
        Action compactSession)
    {
        ArgumentNullException.ThrowIfNull(selectProvider);
        ArgumentNullException.ThrowIfNull(selectModel);
        ArgumentNullException.ThrowIfNull(selectReasoning);
        ArgumentNullException.ThrowIfNull(compactSession);
        return new ModelProviderSelectorController(selectProvider, selectModel, selectReasoning, compactSession);
    }
}
