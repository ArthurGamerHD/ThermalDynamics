using Sandbox.ModAPI;
using VRageMath;

namespace RichHudFramework
{
	namespace UI
	{
		using Client;
		using Server;

		public class CamSpaceNode : HudSpaceNodeBase
		{
			public float PlaneScale { get; set; }

			public Vector3 RotationAxis { get; set; }

			public float RotationAngle { get; set; }

			public Vector3D TransformOffset { get; set; }

			public bool IsScreenSpace { get; set; }

			public bool UseResScaling { get; set; }

/// <summary>CamSpaceNode operation.</summary>
			public CamSpaceNode(HudParentBase parent = null) : base(parent)
			{
				PlaneScale = 1f;
/// <summary>Vector3D operation.</summary>
				TransformOffset = new Vector3D(0.0, 0.0, -MyAPIGateway.Session.Camera.NearPlaneDistance);

				IsScreenSpace = true;
				UseResScaling = true;
			}

/// <summary>Layout operation.</summary>
			protected override void Layout()
			{
				double finalScale = PlaneScale;

				if (IsScreenSpace)
				{
					finalScale *= HudMain.FovScale / HudMain.ScreenHeight;

					if (UseResScaling)
						finalScale *= HudMain.ResScale;
				}

				var scaling = MatrixD.CreateScale(finalScale, finalScale, 1.0);
				var rotation = MatrixD.CreateFromAxisAngle(RotationAxis, RotationAngle);
				var translation = MatrixD.CreateTranslation(TransformOffset);

				PlaneToWorldRef[0] = scaling * rotation * translation * MyAPIGateway.Session.Camera.WorldMatrix;

				base.Layout();
			}
		}
	}
}