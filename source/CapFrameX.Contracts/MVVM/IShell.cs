namespace CapFrameX.Contracts.MVVM
{
	public interface IShell
	{
		dynamic GlobalScreenshotArea { get; }

		bool IsGpuAccelerationActive { get; set; }
	}
}

