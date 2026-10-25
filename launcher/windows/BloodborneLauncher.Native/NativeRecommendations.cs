using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Rendering;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    sealed record RecommendationRow(string Label, string Detail);
    sealed record Recommendation(Hardware Hardware, GpuAdapter? Gpu, IReadOnlyList<RecommendationRow> Rows);
    Recommendation? _recommendation;
    float _recommendationScroll, _recommendationScrollMax;
    void OpenRecommendations()
    {
        var gpu = GpuSelection.Best(GpuAdapters.All());
        var hardware = Hardware.Detect();
        if (gpu != null)
            hardware = hardware with
            {
                Gpu = gpu.Name,
                Vendor = gpu.Vendor,
                VramBytes = gpu.Memory
            };
        var copy = new IniFile();
        foreach (var item in _profile.Ini.Values)
            copy.Set(item.Key, item.Value);
        var rows = hardware.Recommend(_profile.ReadSettings(), copy).Select(c => new RecommendationRow(c.Label, UiText.T(c.From) + "  →  " + UiText.T(c.To))).ToList();
        if (gpu != null)
            rows.Insert(0, new("Rendering GPU", GpuAdapters.Summary(_profile.Ini) + "  →  " + gpu.Label));
        _recommendation = new(hardware, gpu, rows);
        OpenDialogAnimation();
        _recommendationScroll = 0;
        HideNativeText();
        RefreshDialogSurface();
    }

    void ApplyRecommendations() => CloseDialogAnimation(FinishRecommendations);

    void FinishRecommendations()
    {
        var plan = _recommendation;
        if (plan == null)
            return;
        _profile.EditSource((s, ini) =>
        {
            if (plan.Gpu != null)
            {
                ini.Set("gpu_id", plan.Gpu.Index.ToString());
                ini.Set("gpu_name", plan.Gpu.Name);
            }

            foreach (var change in plan.Hardware.Recommend(s, ini))
                change.Apply();
        });
        _recommendation = null;
        _pageRows = null;
        ClearTooltip();
        Rebuild();
        _canvas.ReleaseBuildMetrics();
        _navigationMemory.Request();
    }

    void CloseRecommendations() => CloseDialogAnimation(FinishCloseRecommendations);

    void FinishCloseRecommendations()
    {
        _recommendation = null;
        _recommendationScroll = _recommendationScrollMax = 0;
        RefreshDialogSurface();
    }

    void ScrollRecommendations(float pixels)
    {
        float next = Math.Clamp(_recommendationScroll + pixels, 0, _recommendationScrollMax);
        if (_dialogClosing || next == _recommendationScroll) return;
        _recommendationScroll = next;
        RefreshDialogSurface(lifecycle: false);
    }

    void DrawRecommendations()
    {
        _fixed = true;
        _scrollContent = false;
        _clip = null;
        float width = Math.Min(720 * _scale, DialogArea.Width - 48), x = DialogArea.X + (DialogArea.Width - width) / 2;
        float labelWidth = width * .30f - 24 * _scale, detailWidth = width * .70f - 40 * _scale;
        Span<float> heights = stackalloc float[_recommendation!.Rows.Count];
        float contentHeight = 0;
        for (int i = 0; i < heights.Length; i++)
        {
            var row = _recommendation.Rows[i];
            heights[i] = Math.Max(42 * _scale, Math.Max(WrappedHeight(row.Label, 17, labelWidth),
                WrappedHeight(row.Detail, 16, detailWidth, false, true)) + 10 * _scale);
            contentHeight += heights[i];
        }
        float height = Math.Min(DialogArea.Height - 48, Math.Max(190 * _scale, 126 * _scale + contentHeight));
        float y = DialogArea.Y + (DialogArea.Height - height) / 2;
        var box = new Rect(x, y, width, height);
        DialogBackdrop(box);
        TextAt("Recommended settings", new Rect(x + 24 * _scale, y + 16 * _scale, width - 48 * _scale, 38 * _scale), 23, true);
        var viewport = new Rect(x + 24 * _scale, y + 62 * _scale, width - 48 * _scale, Math.Max(1, height - 126 * _scale));
        _recommendationScrollMax = Math.Max(0, contentHeight - viewport.Height);
        _recommendationScroll = Math.Clamp(_recommendationScroll, 0, _recommendationScrollMax);
        float top = viewport.Y - _recommendationScroll;
        _clip = viewport;
        for (int i = 0; i < heights.Length; i++)
        {
            var row = _recommendation.Rows[i];
            if (top + heights[i] > viewport.Top && top < viewport.Bottom)
            {
                DialogText(row.Label, new Rect(viewport.X, top, labelWidth, heights[i]), 17, Text);
                DialogText(row.Detail, new Rect(x + width * .30f, top, detailWidth, heights[i]), 16, Gold, false);
            }
            top += heights[i];
        }
        _clip = null;
        if (_recommendationScrollMax > 0)
        {
            var track = new Rect(box.Right - 24 * _scale, viewport.Y, 3 * _scale, viewport.Height);
            Fill(track, new Color4(.12f, .13f, .14f, 1));
            Fill(BloodborneLauncher.Native.Interaction.ScrollGeometry.Thumb(track, _recommendationScrollMax, _recommendationScroll),
                new Color4(.35f, .36f, .37f, 1), 1.5f * _scale);
        }
        float button = Math.Min(160 * _scale, (width - 64 * _scale) / 2);
        Button("Cancel", new Rect(box.Center.X - button - 8 * _scale, box.Bottom - 56 * _scale, button, 38 * _scale),
            "recommend-cancel", CloseRecommendations);
        Button("Apply", new Rect(box.Center.X + 8 * _scale, box.Bottom - 56 * _scale, button, 38 * _scale),
            "recommend-apply", ApplyRecommendations);
    }
}
