using VRageMath;

namespace RichHudFramework
{
    namespace UI
    {

        namespace Rendering
        {
            public enum RichCharAccessors : int
            {
                Ch = 1,

                Format = 2,

                Size = 3,

                Offset = 4
            }

			public interface IRichChar
            {
                char Ch { get; }

                GlyphFormat Format { get; }

                Vector2 Size { get; }

                Vector2 Offset { get; }
            }
        }
    }
}