using Godot;

public partial class JumpFlooding : Node2D
{
	[Export] private Texture2D _tex;
	[Export] private RDShaderFile _shader;
	
	public override void _Ready()
	{
		((Window)GetViewport()).FilesDropped += OnFilesDropped;
		InitializeComputePipeline();
		InitializeGui();
	}

	private void OnFilesDropped(string[] files)
	{
		var img = Image.LoadFromFile(files[0]);
		if (img is null) return;
		_tex = ImageTexture.CreateFromImage(img);
		RefreshTexture(_tex);
		FloodingPrepass();
	}

	private int _elapsedPasses = 0;

	public override void _Process(double delta)
	{
		DebugGui();
	}
}
