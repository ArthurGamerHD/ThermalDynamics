namespace Thermodynamics.Presentation
{
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
