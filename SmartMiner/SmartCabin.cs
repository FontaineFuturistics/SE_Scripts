IMyShipController driveController;
IMyShipConnector  driveConnector;
string            lastData = "<none>";

public Program() {
    // find the ship controller (cockpit or remote) named “[DRIVE]…”
    var controllers = new List<IMyShipController>();
    GridTerminalSystem.GetBlocksOfType(controllers, c => c.CustomName.StartsWith("[DRIVE]"));
    if (controllers.Count > 0) driveController = controllers[0];

    // find the connector named “[DRIVE]…”
    var cords = new List<IMyShipConnector>();
    GridTerminalSystem.GetBlocksOfType(cords, c => c.CustomName.StartsWith("[DRIVE]"));
    if (cords.Count > 0) driveConnector = cords[0];

    // run every tick
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Main(string argument, UpdateType updateSource) {
    // Debug info
    Echo($"Controller? {driveController != null}");
    Echo($"Connector?  {driveConnector != null}");
    bool docked = driveConnector != null &&
                  driveConnector.Status == MyShipConnectorStatus.Connected;
    Echo($"Docked?     {docked}");
    Echo($"LastSent:   {lastData}");

    if (!docked) return;

    // collect move/rotate/roll input
    Vector3D mv   = driveController.MoveIndicator;     // X=right, Y=up, Z=forward
    Vector2  rot  = driveController.RotationIndicator; // X=pitch, Y=yaw
    double   roll = driveController.RollIndicator;

    // serialize to CSV
    lastData = $"{mv.X:F3},{mv.Y:F3},{mv.Z:F3},{rot.X:F3},{rot.Y:F3},{roll:F3}";

    // broadcast each tick on “drive”
    IGC.SendBroadcastMessage("drive", lastData);
}
