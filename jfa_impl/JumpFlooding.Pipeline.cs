using System.Runtime.InteropServices;
using Godot;

public partial class JumpFlooding
{
    private int _stepPerFrame = 3;
    private int _maxIteration = 50;
    private bool _lineArtMode = true;

    private const int OpNone = 0;
    private const int OpClick = 1;
    private const int OpClear = 2;
    private const int OpPrepassLine = 4;
    private const int OpPrepassColor = 8;
    private const int OpSeedingMaskBuffer = 16;
    private const int OpFloodingMaskBuffer = 32;
    
    private const int CurrentTexSet = 0;
    private const int PrevTexSet = 1;
    private const int SamplerTexSet = 2;
    private const int MaskTexSet = 3;
    private const int DisplayTexSet = 4;

    private const uint TextureSize = 1024u;

    private struct PushConstants()
    {
        public Vector2I TexSize = Vector2I.One * (int)TextureSize;
        public Vector2I QueryPosition = Vector2I.Zero;
        public int OperationFlag = OpNone;
        public int DisplayMode = 0;
        public int PassCounter = 0;
        public float Threshold = 0.25f;
        public Color ColorTarget = Colors.Green;
    }

    private PushConstants _shaderParameter = new();

    private RenderingDevice _rd;
    private Rid _shaderRd;
    private Rid _pipelineRd;

    private Rid _currentTex;
    private Rid _currentTexUniformSet;
    private Rid _previousTex;
    private Rid _previousTexUniformSet;
    private Rid _displayTex;
    private Rid _displayTexUniformSet;
    private Rid _maskTex;
    private Rid _maskTexUniformSet;

    private Rid _texResource;
    private Rid _texSampler;
    private Rid _texSamplerUniformSet;

    private Texture2Drd _mainTexRd;
    private Texture2Drd _backBufferRd;
    private Texture2Drd _maskBufferRd;
    private Texture2Drd _displayBufferRd;

    private Rid CreateTextureUniformSet(Rid textureRd, uint shaderSet)
    {
        var uniform = new RDUniform();
        uniform.UniformType = RenderingDevice.UniformType.Image;
        uniform.Binding = 0;
        uniform.AddId(textureRd);
        return _rd.UniformSetCreate([uniform], _shaderRd, shaderSet);
    }

    private void RefreshTexture(Texture2D tex)
    {
        if (_texResource.IsValid) _rd.FreeRid(_texResource);
        if (_texSamplerUniformSet.IsValid) _rd.FreeRid(_texSamplerUniformSet);
        var texView = new RDTextureView();
        var texRid = RenderingServer.TextureGetRdTexture(tex.GetRid());
        _texResource = _rd.TextureCreateShared(texView, texRid);
        var texSamplerUniform = new RDUniform();
        texSamplerUniform.UniformType = RenderingDevice.UniformType.SamplerWithTexture;
        texSamplerUniform.AddId(_texSampler);
        texSamplerUniform.AddId(_texResource);
        _texSamplerUniformSet = _rd.UniformSetCreate([texSamplerUniform], _shaderRd, SamplerTexSet);
    }

    private void InitializeComputePipeline()
    {
        _rd = RenderingServer.GetRenderingDevice();
        _shaderRd = _rd.ShaderCreateFromSpirV(_shader.GetSpirV());
        _pipelineRd = _rd.ComputePipelineCreate(_shaderRd);

        var samplerState = new RDSamplerState();
        samplerState.MagFilter = RenderingDevice.SamplerFilter.Linear;
        samplerState.MinFilter = RenderingDevice.SamplerFilter.Linear;
        _texSampler = _rd.SamplerCreate(samplerState);
        var texView = new RDTextureView();
        var texRid = RenderingServer.TextureGetRdTexture(_tex.GetRid());
        _texResource = _rd.TextureCreateShared(texView, texRid);
        var texSamplerUniform = new RDUniform();
        texSamplerUniform.UniformType = RenderingDevice.UniformType.SamplerWithTexture;
        texSamplerUniform.AddId(_texSampler);
        texSamplerUniform.AddId(_texResource);
        _texSamplerUniformSet = _rd.UniformSetCreate([texSamplerUniform], _shaderRd, SamplerTexSet);

        var tfBuffer = new RDTextureFormat();
        tfBuffer.Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat;
        tfBuffer.TextureType = RenderingDevice.TextureType.Type2D;
        tfBuffer.Width = TextureSize;
        tfBuffer.Height = TextureSize;
        tfBuffer.Depth = 1;
        tfBuffer.ArrayLayers = 1;
        tfBuffer.Mipmaps = 1;
        tfBuffer.UsageBits = RenderingDevice.TextureUsageBits.SamplingBit |
                             RenderingDevice.TextureUsageBits.StorageBit |
                             RenderingDevice.TextureUsageBits.CanUpdateBit |
                             RenderingDevice.TextureUsageBits.ColorAttachmentBit |
                             RenderingDevice.TextureUsageBits.CanCopyFromBit;

        var tfDisplay = new RDTextureFormat();
        tfDisplay.Format = RenderingDevice.DataFormat.R8G8B8A8Unorm;
        tfDisplay.TextureType = RenderingDevice.TextureType.Type2D;
        tfDisplay.Width = TextureSize;
        tfDisplay.Height = TextureSize;
        tfDisplay.Depth = 1;
        tfDisplay.ArrayLayers = 1;
        tfDisplay.Mipmaps = 1;
        tfDisplay.UsageBits = RenderingDevice.TextureUsageBits.SamplingBit |
                              RenderingDevice.TextureUsageBits.StorageBit |
                              RenderingDevice.TextureUsageBits.CanUpdateBit |
                              RenderingDevice.TextureUsageBits.ColorAttachmentBit |
                              RenderingDevice.TextureUsageBits.CanCopyFromBit;

        var tfMask = new RDTextureFormat();
        tfMask.Format = RenderingDevice.DataFormat.R8Unorm;
        tfMask.TextureType = RenderingDevice.TextureType.Type2D;
        tfMask.Width = TextureSize;
        tfMask.Height = TextureSize;
        tfMask.Depth = 1;
        tfMask.ArrayLayers = 1;
        tfMask.Mipmaps = 1;
        tfMask.UsageBits = RenderingDevice.TextureUsageBits.SamplingBit |
                           RenderingDevice.TextureUsageBits.StorageBit |
                           RenderingDevice.TextureUsageBits.CanUpdateBit |
                           RenderingDevice.TextureUsageBits.ColorAttachmentBit |
                           RenderingDevice.TextureUsageBits.CanCopyFromBit;

        _currentTex = _rd.TextureCreate(tfBuffer, new RDTextureView(), []);
        _previousTex = _rd.TextureCreate(tfBuffer, new RDTextureView(), []);
        _maskTex = _rd.TextureCreate(tfMask, new RDTextureView(), []);
        _displayTex = _rd.TextureCreate(tfDisplay, new RDTextureView(), []);

        _currentTexUniformSet = CreateTextureUniformSet(_currentTex, CurrentTexSet);
        _previousTexUniformSet = CreateTextureUniformSet(_previousTex, PrevTexSet);
        _displayTexUniformSet = CreateTextureUniformSet(_displayTex, DisplayTexSet);
        _maskTexUniformSet = CreateTextureUniformSet(_maskTex, MaskTexSet);

        _mainTexRd = new Texture2Drd();
        _mainTexRd.TextureRdRid = _currentTex;
        _backBufferRd = new Texture2Drd();
        _backBufferRd.TextureRdRid = _previousTex;
        _maskBufferRd = new Texture2Drd();
        _maskBufferRd.TextureRdRid = _maskTex;
        _displayBufferRd = new Texture2Drd();
        _displayBufferRd.TextureRdRid = _displayTex;
    }

    private void UpdateCompute()
    {
        var pushConstantBytes = MemoryMarshal.AsBytes([_shaderParameter]).ToArray();

        var computeList = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(computeList, _pipelineRd);
        _rd.ComputeListSetPushConstant(computeList, pushConstantBytes, (uint)pushConstantBytes.Length);
        _rd.ComputeListBindUniformSet(computeList, _currentTexUniformSet, CurrentTexSet);
        _rd.ComputeListBindUniformSet(computeList, _previousTexUniformSet, PrevTexSet);
        _rd.ComputeListBindUniformSet(computeList, _texSamplerUniformSet, SamplerTexSet);
        _rd.ComputeListBindUniformSet(computeList, _maskTexUniformSet, MaskTexSet);
        _rd.ComputeListBindUniformSet(computeList, _displayTexUniformSet, DisplayTexSet);
        _rd.ComputeListDispatch(computeList, TextureSize / 8, TextureSize / 8, 1);
        _rd.ComputeListEnd();

        var swapTex = _previousTex;
        var swapUniform = _previousTexUniformSet;
        _previousTex = _currentTex;
        _previousTexUniformSet = _currentTexUniformSet;
        _currentTex = swapTex;
        _currentTexUniformSet = swapUniform;
    }

    private void FloodingPrepass()
    {
        _shaderParameter = _shaderParameter with { OperationFlag = _lineArtMode ? OpPrepassLine : OpPrepassColor};
        UpdateCompute();
        _shaderParameter = _shaderParameter with { OperationFlag = OpSeedingMaskBuffer };
        UpdateCompute();
        _elapsedPasses = 0;
    }

    private void FloodAndDraw()
    {
        if (_elapsedPasses < _maxIteration)
        {
            for (int i = 0; i < _stepPerFrame; i++)
            {
                _shaderParameter = _shaderParameter with
                {
                    OperationFlag = OpFloodingMaskBuffer,
                    PassCounter = _elapsedPasses
                };
                UpdateCompute();
                _elapsedPasses += 1;
            }
        }
        else
        {
            _shaderParameter = _shaderParameter with
            {
                OperationFlag = Input.IsMouseButtonPressed(MouseButton.Right) ? OpClick : OpNone
            };
            UpdateCompute();
        }
    }
}