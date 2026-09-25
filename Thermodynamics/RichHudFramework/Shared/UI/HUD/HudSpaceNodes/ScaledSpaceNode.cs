using System;
namespace RichHudFramework
{
	namespace UI
	{
		public class ScaledSpaceNode : HudSpaceNodeBase
		{
			public float PlaneScale { get; set; } = 1f;

			public Func<float> UpdateScaleFunc { get; set; }


			public ScaledSpaceNode(HudParentBase parent = null) : base(parent)
			{ }


			protected override void Layout()
			{
				if (UpdateScaleFunc != null)

					PlaneScale = UpdateScaleFunc();

				IReadOnlyHudSpaceNode parentSpace = Parent.HudSpace;

				PlaneToWorldRef[0] = parentSpace.PlaneToWorldRef[0];
				PlaneToWorldRef[0].Right *= PlaneScale;
				PlaneToWorldRef[0].Up *= PlaneScale;

				IsInFront = parentSpace.IsInFront;
				IsFacingCamera = parentSpace.IsFacingCamera;

				CursorPos = parentSpace.CursorPos / PlaneScale;
			}
		}
	}
}