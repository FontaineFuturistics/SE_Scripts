// --- Fields ---
Dictionary<char, List<IMyShipConnector>> connectorsBySet = new Dictionary<char, List<IMyShipConnector>>();
Dictionary<char, List<IMyInteriorLight>> lightsBySet     = new Dictionary<char, List<IMyInteriorLight>>();

// --- Constructor (runs once on script load) ---
public Program() {
    Runtime.UpdateFrequency = UpdateFrequency.Update10;  // every 10 ticks
    Initialize();
}

// --- Initialization: find and group connectors + lights ---
void Initialize() {
    connectorsBySet.Clear();
    lightsBySet.Clear();

    List<IMyTerminalBlock> allBlocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(allBlocks);

    foreach (var block in allBlocks) {
        string name = block.CustomName;
        // look for “[CONN X]” prefix where X is any char
        if (name.StartsWith("[CONN ") && name.Length > 7 && name[7] == ']' && block.CubeGrid == Me.CubeGrid) {
            char setId = name[6];

            if (block is IMyShipConnector) {
                if (!connectorsBySet.ContainsKey(setId))
                    connectorsBySet[setId] = new List<IMyShipConnector>();
                connectorsBySet[setId].Add((IMyShipConnector)block);
            }

            if (block is IMyInteriorLight) {
                if (!lightsBySet.ContainsKey(setId))
                    lightsBySet[setId] = new List<IMyInteriorLight>();
                lightsBySet[setId].Add((IMyInteriorLight)block);
            }
        }
    }
}

// --- Main: runs every Update10 tick ---
public void Main(string argument, UpdateType updateSource) {
    foreach (var kvp in connectorsBySet)
    {
        char setId = kvp.Key;
        var conns = kvp.Value;

        // skip if no lights for this set
        if (!lightsBySet.ContainsKey(setId)) continue;
        var lights = lightsBySet[setId];

        bool allConnected = true;
        bool allConnectable = true;
        bool allNeither = true;

        // inspect each connector’s status
        foreach (var c in conns)
        {
            var st = c.Status;
            if (st != MyShipConnectorStatus.Connected) allConnected = false;
            if (st != MyShipConnectorStatus.Connectable) allConnectable = false;
            if (st == MyShipConnectorStatus.Connected
             || st == MyShipConnectorStatus.Connectable) allNeither = false;
        }

        // decide color & enable
        Color targetColor;
        bool enableLight = true;

        if (allConnected) targetColor = new Color(0, 255, 0); // green
        else if (allConnectable) targetColor = new Color(255, 255, 0); // yellow
        else if (allNeither) { enableLight = false; targetColor = new Color(0, 0, 0); } // off
        else targetColor = new Color(255, 0, 0); // red

        // apply to each light
        foreach (var l in lights)
        {
            l.Color = targetColor;
            l.Enabled = enableLight;
        }
    }
}
