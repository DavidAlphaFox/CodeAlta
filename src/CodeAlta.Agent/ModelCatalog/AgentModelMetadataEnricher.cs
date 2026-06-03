namespace CodeAlta.Agent.ModelCatalog;

// 模块功能：用 models.dev 目录数据和用户覆盖配置为模型列表补充元数据（能力、名称、描述等）
internal static class AgentModelMetadataEnricher
{
    // 函数功能：批量丰富模型列表，对每个模型应用目录数据与覆盖配置
    public static IReadOnlyList<AgentModelInfo> EnrichModels(
        IReadOnlyList<AgentModelInfo> models,
        ModelsDevCatalogService? catalog,
        string? modelsDevProviderId,
        IReadOnlyDictionary<string, AgentModelOverride>? overrides)
    {
        if (models.Count == 0)
        {
            return models;
        }

        return models
            .Select(model => EnrichModel(model, catalog, modelsDevProviderId, overrides))
            .ToArray();
    }

    // 函数功能：对单个模型依次应用 models.dev 目录元数据和用户覆盖配置，若无变化则返回原对象
    public static AgentModelInfo EnrichModel(
        AgentModelInfo model,
        ModelsDevCatalogService? catalog,
        string? modelsDevProviderId,
        IReadOnlyDictionary<string, AgentModelOverride>? overrides)
    {
        ArgumentNullException.ThrowIfNull(model);

        var displayName = model.DisplayName;
        var description = model.Description;
        var capabilities = model.Capabilities is null
            ? null
            : new Dictionary<string, object?>(model.Capabilities, StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(modelsDevProviderId) &&
            catalog?.TryGetModel(modelsDevProviderId, model.Id, out var modelsDevModel) == true)
        {
            capabilities ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            ApplyModelsDevMetadata(capabilities, modelsDevProviderId, modelsDevModel);

            if (string.IsNullOrWhiteSpace(displayName) ||
                string.Equals(displayName, model.Id, StringComparison.Ordinal))
            {
                displayName = modelsDevModel.Name ?? displayName;
            }
        }

        if (TryGetOverride(overrides, model, out var modelOverride) && modelOverride is not null)
        {
            capabilities ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            ApplyOverride(capabilities, modelOverride);

            if (!string.IsNullOrWhiteSpace(modelOverride.DisplayName))
            {
                displayName = modelOverride.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(modelOverride.Description))
            {
                description = modelOverride.Description;
            }
        }

        if (capabilities is null &&
            string.Equals(displayName, model.DisplayName, StringComparison.Ordinal) &&
            string.Equals(description, model.Description, StringComparison.Ordinal))
        {
            return model;
        }

        return new AgentModelInfo(
            model.Id,
            DisplayName: displayName,
            Description: description,
            Provider: model.Provider,
            DefaultReasoningEffort: model.DefaultReasoningEffort,
            SupportedReasoningEfforts: model.SupportedReasoningEfforts,
            Capabilities: capabilities ?? model.Capabilities);
    }

    // 函数功能：按模型 Id、DisplayName 和模糊键依次查找覆盖配置，找到则输出配置并返回 true
    private static bool TryGetOverride(
        IReadOnlyDictionary<string, AgentModelOverride>? overrides,
        AgentModelInfo model,
        out AgentModelOverride? modelOverride)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (overrides is null || overrides.Count == 0)
        {
            modelOverride = null;
            return false;
        }

        if (overrides.TryGetValue(model.Id, out modelOverride))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(model.DisplayName) &&
            overrides.TryGetValue(model.DisplayName, out modelOverride))
        {
            return true;
        }

        var lookupKeys = AgentModelIdentity.GetLookupKeySet(model.Id, model.DisplayName);
        foreach (var entry in overrides)
        {
            if (AgentModelIdentity.Matches(entry.Key, lookupKeys))
            {
                modelOverride = entry.Value;
                return true;
            }
        }

        modelOverride = null;
        return false;
    }

    // 函数功能：将 models.dev 目录中的模型定义写入能力字典（仅填充缺失字段，不覆盖已有值）
    private static void ApplyModelsDevMetadata(
        IDictionary<string, object?> capabilities,
        string modelsDevProviderId,
        ModelsDevModelDefinition model)
    {
        capabilities["modelsDevProviderId"] = modelsDevProviderId;
        capabilities["modelsDevModelId"] = model.Id;
        SetIfMissing(capabilities, "family", model.Family);
        SetIfMissing(capabilities, "knowledge", model.Knowledge);
        SetIfMissing(capabilities, "releaseDate", model.ReleaseDate);
        SetIfMissing(capabilities, "lastUpdated", model.LastUpdated);
        SetIfMissing(capabilities, "status", model.Status);
        SetIfMissing(capabilities, "supportsAttachments", model.Attachment);
        SetIfMissing(capabilities, "supportsReasoning", model.Reasoning);
        SetIfMissing(capabilities, "supportsToolCall", model.ToolCall);
        SetIfMissing(capabilities, "supportsStructuredOutput", model.StructuredOutput);
        SetIfMissing(capabilities, "supportsTemperature", model.Temperature);
        SetIfMissing(capabilities, "openWeights", model.OpenWeights);
        SetIfMissing(capabilities, "inputModalities", model.Modalities?.Input);
        SetIfMissing(capabilities, "outputModalities", model.Modalities?.Output);
        SetIfMissing(capabilities, "contextWindow", model.Limit?.Context);
        SetIfMissing(capabilities, "contextWindowTokens", model.Limit?.Context);
        SetIfMissing(capabilities, "inputTokenLimit", model.Limit?.Input);
        SetIfMissing(capabilities, "maxInputTokens", model.Limit?.Input);
        SetIfMissing(capabilities, "outputTokenLimit", model.Limit?.Output);
        SetIfMissing(capabilities, "maxTokens", model.Limit?.Output);
    }

    // 函数功能：将用户覆盖配置写入能力字典，有值时强制覆盖已有字段
    private static void ApplyOverride(
        IDictionary<string, object?> capabilities,
        AgentModelOverride modelOverride)
    {
        capabilities["overrideApplied"] = true;
        SetWhenValue(capabilities, "contextWindow", modelOverride.ContextWindowTokens);
        SetWhenValue(capabilities, "contextWindowTokens", modelOverride.ContextWindowTokens);
        SetWhenValue(capabilities, "inputTokenLimit", modelOverride.InputTokenLimit);
        SetWhenValue(capabilities, "maxInputTokens", modelOverride.InputTokenLimit);
        SetWhenValue(capabilities, "outputTokenLimit", modelOverride.OutputTokenLimit);
        SetWhenValue(capabilities, "maxTokens", modelOverride.MaxTokens ?? modelOverride.OutputTokenLimit);
        SetWhenValue(capabilities, "supportsReasoning", modelOverride.SupportsReasoning);
        SetWhenValue(capabilities, "supportsToolCall", modelOverride.SupportsToolCall);
        SetWhenValue(capabilities, "supportsAttachments", modelOverride.SupportsAttachments);
        SetWhenValue(capabilities, "supportsStructuredOutput", modelOverride.SupportsStructuredOutput);
    }

    // 函数功能：仅当键不存在且值非 null 时才向能力字典写入
    private static void SetIfMissing(IDictionary<string, object?> capabilities, string key, object? value)
    {
        if (value is null || capabilities.ContainsKey(key))
        {
            return;
        }

        capabilities[key] = value;
    }

    // 函数功能：仅当值非 null 时向能力字典写入（允许覆盖已有值）
    private static void SetWhenValue(IDictionary<string, object?> capabilities, string key, object? value)
    {
        if (value is not null)
        {
            capabilities[key] = value;
        }
    }
}
