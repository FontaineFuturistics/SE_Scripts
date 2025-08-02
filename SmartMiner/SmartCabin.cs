IMyShipController driveController;
IMyShipConnector  driveConnector;
string            lastData = "<none>";

public Program() {
    // find the ship controller (cockpit or remote) named “[DRIVE]…”
    var controllers = new List<IMyShipController>();
    GridTerminalSystem.GetBlocksOfType(
        controllers,
        c => c.CustomName.StartsWith("[DRIVE]") 
          && c.CubeGrid == Me.CubeGrid
    );
    if (controllers.Count > 0)
        driveController = controllers[0];

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

    //if (!docked) return; // It turns out we can always broadcast, even when undocked

    // collect move/rotate/roll input
    Vector3D mv   = driveController.MoveIndicator;     // X=right, Y=up, Z=forward
    Vector2  rot  = driveController.RotationIndicator; // X=pitch, Y=yaw
    double   roll = driveController.RollIndicator;

    // New braking code
    if (driveController.DampenersOverride)
    {
        // grab current velocities
        var shipVel    = driveController.GetShipVelocities();
        Vector3D worldVel    = shipVel.LinearVelocity;
        Vector3D worldAngVel = shipVel.AngularVelocity;

        // convert into cockpit‐local space
        var localVel = Vector3D.TransformNormal(
            worldVel,
            MatrixD.Transpose(driveController.WorldMatrix)
        );

        const double threshold = 0.1;

        if (Math.Abs(localVel.X) > threshold && Math.Abs(mv.X) < threshold)
            mv.X = (float)(-localVel.X / Math.Abs(localVel.X));

        if (Math.Abs(localVel.Y) > threshold && Math.Abs(mv.Y) < threshold)
            mv.Y = (float)(-localVel.Y / Math.Abs(localVel.Y));

        if (Math.Abs(localVel.Z) > threshold && Math.Abs(mv.Z) < threshold)
            mv.Z = (float)(-localVel.Z / Math.Abs(localVel.Z));
    }
    // End new braking code

    // serialize to CSV
    lastData = $"{mv.X:F3},{mv.Y:F3},{mv.Z:F3},{rot.X:F3},{rot.Y:F3},{roll:F3}";

    // broadcast each tick on “drive”
    IGC.SendBroadcastMessage("drive", lastData);
}
