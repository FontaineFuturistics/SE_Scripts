IMyShipController driveController;
IMyShipConnector driveConnector;
List<IMyThrust> driveThrusters;
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
    GridTerminalSystem.GetBlocksOfType(cords, c => c.CustomName.StartsWith("[DRIVE]") && c.CubeGrid == Me.CubeGrid);
    if (cords.Count > 0) driveConnector = cords[0];

    // Get all [DRIVE] thrusters on this grid
    driveThrusters = new List<IMyThrust>();
    GridTerminalSystem.GetBlocksOfType(
        driveThrusters,
        t => t.CubeGrid == Me.CubeGrid 
          && t.CustomName.StartsWith("[DRIVE]")
    );

    // run every tick
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Main(string argument, UpdateType updateSource) {
    bool docked = driveConnector != null &&
                  driveConnector.Status == MyShipConnectorStatus.Connected;

    // Check that we are docked to a [DRIVE] connector
    bool driveDocked = false;
    if (docked)
    {
        var remoteConn = driveConnector.OtherConnector as IMyShipConnector;
        if (remoteConn != null && remoteConn.CustomName.StartsWith("[DRIVE]"))
            driveDocked = true;
    }

    // Disable all [DRIVE] thrusters when docked to a [DRIVE] connector so we don't burn the grid
    foreach (var thr in driveThrusters) thr.Enabled = !docked;

    // collect move/rotate/roll input
    Vector3D mv   = driveController.MoveIndicator;     // X=right, Y=up, Z=forward
    Vector2  rot  = driveController.RotationIndicator; // X=pitch, Y=yaw
    double   roll = driveController.RollIndicator;

    // Check if there is input
    bool hasInput = Math.Abs(mv.X) > 0.01 || Math.Abs(mv.Y) > 0.01 || Math.Abs(mv.Z) > 0.01 ||
                    Math.Abs(rot.X) > 0.01 || Math.Abs(rot.Y) > 0.01 || Math.Abs(roll) > 0.01;

    // Auto braking
    if (driveController.DampenersOverride && !hasInput)
    {
        // grab current velocities
        var shipVel = driveController.GetShipVelocities();
        Vector3D worldVel = shipVel.LinearVelocity;
        Vector3D worldAngVel = shipVel.AngularVelocity;

        // convert into cockpit-local space
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

    // serialize to CSV
    lastData = $"{mv.X:F3},{mv.Y:F3},{mv.Z:F3},{rot.X:F3},{rot.Y:F3},{roll:F3}";

    // Determine the correct channel and send the message
    string channel = "";
    if (driveDocked)
    {
        var remoteConn = driveConnector.OtherConnector as IMyShipConnector;
        if (remoteConn != null)
            channel = "drive-" + remoteConn.EntityId;
    }

    // Log
    Echo($"Found Controller? {driveController != null}");
    Echo($"Found Connector?  {driveConnector != null}");
    Echo($"Docked?     {docked}");
    Echo($"Drive Docked? {driveDocked}");
    Echo($"LastSent:   {lastData}");
    Echo($"Channel:    {channel}");

    // broadcast only when docked and we have a channel
    if (!string.IsNullOrEmpty(channel))
        IGC.SendBroadcastMessage(channel, lastData);
}
