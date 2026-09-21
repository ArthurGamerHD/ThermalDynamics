using VRageMath;

namespace RichHudFramework
{
    namespace UI
    {
        namespace Rendering
        {
            public enum LineAccessors : int
            {
                Count = 1,

                Size = 2,

                VerticalOffset = 3,
            }

            public interface ILine : IIndexedCollection<IRichChar>
            {
				new IRichChar this[int index] { get; }

				Vector2 Size { get; }

                float VerticalOffset { get; }
            }
        }
    }
}