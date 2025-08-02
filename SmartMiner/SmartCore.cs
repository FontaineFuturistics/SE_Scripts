IMyShipConnector         driveConnector;
IMyRemoteControl         driveRemote;
IMyBroadcastListener     driveListener;

List<IMyThrust> forwardThrusters  = new List<IMyThrust>();
List<IMyThrust> backwardThrusters = new List<IMyThrust>();
List<IMyThrust> upThrusters       = new List<IMyThrust>();
List<IMyThrust> downThrusters     = new List<IMyThrust>();
List<IMyThrust> leftThrusters     = new List<IMyThrust>();
List<IMyThrust> rightThrusters    = new List<IMyThrust>();

List<IMyGyro> gyros = new List<IMyGyro>();

Vector3D currentTranslation = Vector3D.Zero;
Vector3D currentRotation    = Vector3D.Zero;
string   lastMessage        = "<none>";

public Program() {
    // find the [DRIVE] connector
    var cords = new List<IMyShipConnector>();
    GridTerminalSystem.GetBlocksOfType(cords, c => c.CustomName.StartsWith("[DRIVE]"));
    if (cords.Count > 0) driveConnector = cords[0];

    // find the [DRIVE] remote control
    var remotes = new List<IMyRemoteControl>();
    GridTerminalSystem.GetBlocksOfType(remotes, r => r.CustomName.StartsWith("[DRIVE]"));
    if (remotes.Count > 0) {
        driveRemote = remotes[0];
        // ensure you can auto-hold when undocked
        driveRemote.DampenersOverride = true;
    }

    // subscribe to “drive” broadcasts
    driveListener = IGC.RegisterBroadcastListener("drive");
    driveListener.SetMessageCallback("drive");

    // gather thrusters belonging strictly to this grid
    var allThrusters = new List<IMyThrust>();
    GridTerminalSystem.GetBlocksOfType(allThrusters);
    foreach (var thr in allThrusters) {
        if (thr.CubeGrid != Me.CubeGrid) continue;
        var bw = thr.WorldMatrix.Backward;
        if      (bw.Dot(driveRemote.WorldMatrix.Forward)  > 0.75) forwardThrusters.Add(thr);
        else if (bw.Dot(driveRemote.WorldMatrix.Backward) > 0.75) backwardThrusters.Add(thr);
        else if (bw.Dot(driveRemote.WorldMatrix.Up)       > 0.75) upThrusters.Add(thr);
        else if (bw.Dot(driveRemote.WorldMatrix.Down)     > 0.75) downThrusters.Add(thr);
        else if (bw.Dot(driveRemote.WorldMatrix.Left)     > 0.75) leftThrusters.Add(thr);
        else if (bw.Dot(driveRemote.WorldMatrix.Right)    > 0.75) rightThrusters.Add(thr);
    }

    // gather gyros on this grid
    GridTerminalSystem.GetBlocksOfType(gyros, g => g.CubeGrid == Me.CubeGrid);

    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Main(string argument, UpdateType updateSource) {
    // Debug info
    Echo($"Remote?    {driveRemote != null}");
    Echo($"Connector? {driveConnector != null}");
    bool docked = driveConnector != null &&
                  driveConnector.Status == MyShipConnectorStatus.Connected;
    Echo($"Docked?    {docked}");
    Echo($"LastMsg:   {lastMessage}");

    // grab any pending “drive” broadcasts
    while (driveListener.HasPendingMessage) {
        var msg = driveListener.AcceptMessage();
        lastMessage = msg.Data as string ?? "<invalid>";
        var parts = lastMessage.Split(',');
        if (parts.Length == 6) {
            currentTranslation = new Vector3D(
                double.Parse(parts[0]), 
                double.Parse(parts[1]), 
                double.Parse(parts[2]));
            currentRotation = new Vector3D(
                double.Parse(parts[3]), 
                double.Parse(parts[4]), 
                double.Parse(parts[5]));
        }
    }

    if (docked) {
        driveRemote.SetAutoPilotEnabled(false);
        driveRemote.DampenersOverride = false;
        ApplyMovement();
    } else {
        ClearMovement();
        driveRemote.DampenersOverride = true;
        driveRemote.ClearWaypoints();
        driveRemote.AddWaypoint(driveRemote.GetPosition(), "Hold");
        driveRemote.SetAutoPilotEnabled(true);
    }
}

void ApplyMovement()
{
    // invert forward/backward only
    float fwd = (float)currentTranslation.Z;
    float up = (float)currentTranslation.Y;
    float right = (float)currentTranslation.X;

    Action<List<IMyThrust>, float> setT = (list, val) =>
    {
        foreach (var t in list)
            t.ThrustOverridePercentage = MathHelper.Clamp(val, 0f, 1f);
    };

    // swapped the sign here
    setT(forwardThrusters, Math.Max(0, -fwd));
    setT(backwardThrusters, Math.Max(0, fwd));
    setT(upThrusters, Math.Max(0, up));
    setT(downThrusters, Math.Max(0, -up));
    setT(rightThrusters, Math.Max(0, right));
    setT(leftThrusters, Math.Max(0, -right));

    foreach (var g in gyros) {
        g.GyroOverride = true;
        var local = Vector3D.TransformNormal(
           currentRotation, 
           Matrix.Transpose(g.WorldMatrix.GetOrientation()));
        g.Pitch = (float)local.X;
        g.Yaw   = (float)local.Y;
        g.Roll  = (float)local.Z;
    }
    // foreach (var g in gyros) {
    //     g.GyroOverride = true;

    //     // 1) Grab the full world‐matrix of this gyro
    //     var world = g.WorldMatrix;

    //     // 2) Remove translation so we only have rotation
    //     world.Translation = Vector3D.Zero;

    //     // 3) Invert rotation by transposing (orthonormal matrix)
    //     var invRot = MatrixD.Transpose(world);

    //     // 4) Rotate our ship‐local rotation into gyro‐local axes
    //     var local = Vector3D.TransformNormal(currentRotation, invRot);

    //     g.Pitch = (float)local.X;
    //     g.Yaw = (float)local.Y;
    //     g.Roll = (float)local.Z;
    // }
}

void ClearMovement() {
    // zero thrusters
    foreach (var t in forwardThrusters
                   .Concat(backwardThrusters)
                   .Concat(upThrusters)
                   .Concat(downThrusters)
                   .Concat(leftThrusters)
                   .Concat(rightThrusters))
    {
        t.ThrustOverridePercentage = 0f;
    }
    // disable gyros
    foreach (var g in gyros) g.GyroOverride = false;
}
