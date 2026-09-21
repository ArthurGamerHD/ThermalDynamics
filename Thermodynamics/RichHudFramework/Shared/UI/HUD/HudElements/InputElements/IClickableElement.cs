namespace RichHudFramework
{
    namespace UI
    {
        using Client;
        using Server;

        public interface IMouseInput : IFocusableElement
        {
            event EventHandler CursorEntered;

            event EventHandler CursorExited;

            event EventHandler LeftClicked;

            event EventHandler LeftReleased;

            event EventHandler RightClicked;

            event EventHandler RightReleased;

            EventHandler CursorEnteredCallback { set; }

            EventHandler CursorExitedCallback { set; }

            EventHandler LeftClickedCallback { set; }

            EventHandler LeftReleasedCallback { set; }

            EventHandler RightClickedCallback { set; }

            EventHandler RightReleasedCallback { set; }

            bool RequestCursor { get; set; }

            ToolTip ToolTip { get; set; }

            bool IsLeftClicked { get; }

            bool IsRightClicked { get; }

            bool IsNewLeftClicked { get; }

            bool IsNewRightClicked { get; }

            bool IsLeftReleased { get; }

            bool IsRightReleased { get; }

            bool IsMousedOver { get; }
        }

        public interface IClickableElement : IFocusableElement
        {
            IMouseInput MouseInput { get; }
        }
    }
}