using VRageMath;

namespace RichHudFramework
{
	namespace UI
	{
		public interface IReadOnlyHudElement : IReadOnlyHudNode
		{
			Vector2 Size { get; }

			float Height { get; }

			float Width { get; }

			Vector2 Origin { get; }

			Vector2 Offset { get; }

			ParentAlignments ParentAlignment { get; }

			DimAlignments DimAlignment { get; }

			bool UseCursor { get; }

			bool ShareCursor { get; }

			bool IsMousedOver { get; }
		}
	}
}