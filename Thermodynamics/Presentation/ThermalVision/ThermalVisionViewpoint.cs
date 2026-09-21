namespace Thermodynamics.Presentation
{
    /// <summary>Client view facts shared by the engine adapter and offline eligibility regressions.</summary>
    public struct ThermalVisionViewpoint
    {
        public bool IsClient;
        public bool HasSessionCamera;
        public bool HasLocalPlayer;
        public bool PlayerCharacterDead;
        public long ControllerEntityId;
        public bool ControllerIsCamera;
        public bool CameraActiveLocal;
        public bool CameraWorking;
        public bool CameraClosing;
        public long CharacterEntityId;
        public bool CharacterClosing;
        public bool FirstPerson;
        public long ControlledEntityId;

        /// <summary>Zero means ineligible. A camera must be the locally active view;
        /// a suit must be both the camera owner and the controlled entity.</summary>
        public long EligibleEntityId
        {
            get
            {
                if (!IsClient || !HasSessionCamera || !HasLocalPlayer || PlayerCharacterDead
                    || ControllerEntityId == 0) return 0;
                if (ControllerIsCamera)
                    return CameraActiveLocal && CameraWorking && !CameraClosing ? ControllerEntityId : 0;
                return CharacterEntityId != 0 && !CharacterClosing && FirstPerson
                    && ControllerEntityId == CharacterEntityId && ControlledEntityId == CharacterEntityId
                    ? CharacterEntityId : 0;
            }
        }
    }
}
