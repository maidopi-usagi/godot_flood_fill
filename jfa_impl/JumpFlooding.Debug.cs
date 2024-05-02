using Godot;
using GodotTask;
using ImGuiGodot;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

public partial class JumpFlooding
{
    private async void InitializeGui()
    {
        await GDTask.Delay(10);

        var screenSize = ((Window)GetViewport()).Size;
        ImGui.SetWindowSize("Stats", new Vector2(256.0f, 128.0f));
        ImGui.SetWindowPos("Stats", new Vector2(screenSize.X * 0.67f, screenSize.Y * 0.1f));
        ImGui.SetWindowSize("Params", new Vector2(256.0f, 128.0f));
        ImGui.SetWindowPos("Params", new Vector2(screenSize.X * 0.8f, screenSize.Y * 0.25f));
        ImGui.SetWindowSize("Matte", new (512, 580));
        ImGui.SetWindowPos("Matte", new(16.0f));
        ImGui.SetWindowSize("BackBuffer", new (256));
        ImGui.SetWindowPos("BackBuffer", new(screenSize.X * 0.33f, screenSize.Y * 0.5f));
        ImGui.SetWindowSize("OutputResult", new (256));
        ImGui.SetWindowPos("OutputResult", new(screenSize.X * 0.67f, screenSize.Y * 0.5f));
        
        FloodingPrepass();
    }
    
    private void DebugGui()
    {
        if (ImGui.Begin("Stats"))
        {
            ImGui.Text($"RenderFPS:{Engine.GetFramesPerSecond()}");
            ImGui.Text($"Canvas Size: {new Vector2(TextureSize, TextureSize)}");
            ImGui.Text($"ElapsedPasses: {_elapsedPasses}");
            ImGui.ProgressBar((float)_elapsedPasses / _maxIteration, new (0.0f));
            ImGui.SameLine();
            ImGui.Text("Progress");
            ImGui.Text($"QueryPos: {_shaderParameter.QueryPosition}");
            ImGui.End();
        }
        
        if (ImGui.Begin("Matte"))
        {
            ImGui.Text("Drag and drop any image file to replace source texture");
            ImGui.Text("Press RMB on image below to pick some matte");
            var displayMode = _shaderParameter.DisplayMode;
            if (ImGui.SliderInt("Display Mode", ref displayMode, 0, 2))
            {
                _shaderParameter = _shaderParameter with { DisplayMode = displayMode };
            }
            var width = ImGui.GetWindowWidth();
            var size = new Vector2(width);
            Widgets.Image(_displayBufferRd, size);
            var rectMax = ImGui.GetItemRectMax();
            var rectMin = ImGui.GetItemRectMin();
            var rectSize = ImGui.GetItemRectSize();
            var localPos = (ImGui.GetMousePos() - rectMin) / rectSize * new Vector2(TextureSize, TextureSize);
            _shaderParameter = _shaderParameter with { QueryPosition = new Vector2I((int)localPos.X, (int)localPos.Y) };
            ImGui.End();
        }

        if (ImGui.Begin("BackBuffer"))
        {
            var width = ImGui.GetWindowWidth();
            var size = new Vector2(width);
            Widgets.Image(_backBufferRd, size);
            ImGui.End();
        }
        
        if (ImGui.Begin("OutputResult"))
        {
            var width = ImGui.GetWindowWidth();
            var size = new Vector2(width);
            Widgets.Image(_maskBufferRd, size);
            ImGui.End();
        }

        if (ImGui.Begin("Params"))
        {
            ImGui.SliderInt("Steps/Frame", ref _stepPerFrame, 1, 10);
            ImGui.SliderInt("Total Iterations", ref _maxIteration, 1, 100);
            
            if (ImGui.Checkbox("Line Art Mode", ref _lineArtMode))
            {
                FloodingPrepass();
            }

            if (!_lineArtMode)
            {
                var color = new Vector4(_shaderParameter.ColorTarget.R, _shaderParameter.ColorTarget.G, _shaderParameter.ColorTarget.B,
                    _shaderParameter.ColorTarget.A);
                if (ImGui.ColorEdit4("Color Target", ref color))
                {
                    _shaderParameter.ColorTarget = new Color(color.X, color.Y, color.Z, color.W);
                    FloodingPrepass();
                }
            }
            var data = _shaderParameter.Threshold;
            if (ImGui.SliderFloat("Threshold", ref data, 0.0f, 1.0f))
            {
                _shaderParameter = _shaderParameter with { Threshold = data };
                FloodingPrepass();
            }
            else
            {
                FloodAndDraw();
            }
            ImGui.End();
        }
    }
}
