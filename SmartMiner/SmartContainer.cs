public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
}

public void Main(string argument, UpdateType updateSource)
{
    // Get reference to this programmable block's grid
    var myGrid = Me.CubeGrid;

    // Check connectors only on local grid
    var connectors = new List<IMyShipConnector>();
    GridTerminalSystem.GetBlocksOfType(connectors, c => c.CubeGrid == myGrid);
    bool isConnected = connectors.Any(c => c.Status == MyShipConnectorStatus.Connected);

    // Get thrusters, gyros, and remote controls on local grid
    var thrusters = new List<IMyThrust>();
    GridTerminalSystem.GetBlocksOfType(thrusters, t => t.CubeGrid == myGrid);

    var gyros = new List<IMyGyro>();
    GridTerminalSystem.GetBlocksOfType(gyros, g => g.CubeGrid == myGrid);

    var remoteControls = new List<IMyRemoteControl>();
    GridTerminalSystem.GetBlocksOfType(remoteControls, rc => rc.CubeGrid == myGrid);

    if (!isConnected)
    {
        foreach (var thruster in thrusters)
            thruster.Enabled = true;

        foreach (var gyro in gyros)
            gyro.Enabled = true;

        foreach (var rc in remoteControls)
        {
            rc.ClearWaypoints();
            rc.AddWaypoint(rc.GetPosition(), "Hold Position");
            rc.FlightMode = FlightMode.OneWay;
            rc.SetDockingMode(false);
            rc.SetCollisionAvoidance(false);
            rc.SetAutoPilotEnabled(true);
        }
    }
    else
    {
        foreach (var thruster in thrusters)
            thruster.Enabled = false;

        foreach (var gyro in gyros)
            gyro.Enabled = false;

        foreach (var rc in remoteControls)
            rc.SetAutoPilotEnabled(false);
    }
}
