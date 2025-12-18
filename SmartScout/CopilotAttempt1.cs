// Autonomous Survey Drone Script for Space Engineers
// No usings, namespaces or additional classes allowed

//—————— CONSTANTS ——————
const double SPIRAL_SPACING        = 1000.0;      // meters between spiral arms
const double DETECT_DISTANCE       = 400.0;       // meters to trigger survey
const double ORBIT_DISTANCE        = 20.0;        // meters from surface
const int    ORBIT_SEGMENTS        = 36;          // segments per orbit

//—————— STATE ENUM ——————
enum Routine
{
    None = 0,
    Search,
    Survey,
    PhoneHome,
    GoTo
}

//—————— FIELDS ——————
Routine               currentRoutine     = Routine.None;
string                returnRoutineData  = "";
Vector3D              origin             = Vector3D.Zero;
bool                  originLoaded       = false;
Vector3D              lastPhoneHomePos   = Vector3D.Zero;

// Search state
int                   spiralLayer        = 1;
int                   segmentTurnCount   = 0;
int                   segmentIndex       = 0;
double                segmentLength      = SPIRAL_SPACING;
List<Vector3D>        spiralWaypoints    = new List<Vector3D>();

// Survey state
List<long>            pendingAsteroids   = new List<long>();
int                   surveyAsteroidIdx  = 0;
Vector3D              surveyStartPos     = Vector3D.Zero;
List<string>          surveyOreData      = new List<string>();

// PhoneHome state
string                phoneHomeMessage   = "";
Vector3D              phoneHomeTarget    = Vector3D.Zero;
long                  phoneHomeChannel   = 0;
bool                  waitingForAck      = false;

// GoTo state
Vector3D              gotoTarget         = Vector3D.Zero;

// Blocks
IMyLaserAntenna       laser;
IMyOreDetector        oreDetector;
IMyTextPanel          textPanel;
IMyRemoteControl      remote;
List<IMySensorBlock>  sensors             = new List<IMySensorBlock>();

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    LoadState();
    GatherBlocks();
    if (!originLoaded)
        SetupOrigin();
    
    // If no routine is active, start in Search
    if (currentRoutine == Routine.None)
        currentRoutine = Routine.Search;
}

// Main entry per tick
public void Main(string argument, UpdateType updateSource)
{
    LoadState();
    RunGeneralRoutine();
    
    switch (currentRoutine)
    {
        case Routine.Search:
            RunSearchRoutine();
            break;
        case Routine.Survey:
            RunSurveyRoutine();
            break;
        case Routine.PhoneHome:
            RunPhoneHomeRoutine();
            break;
        case Routine.GoTo:
            RunGoToRoutine();
            break;
    }
    
    SaveState();
}

//———————— BLOCK GATHERING ————————
void GatherBlocks()
{
    laser       = GridTerminalSystem.GetBlockWithName("LaserAntenna") as IMyLaserAntenna;
    oreDetector = GridTerminalSystem.GetBlockWithName("OreDetector") as IMyOreDetector;
    textPanel   = GridTerminalSystem.GetBlockWithName("OriginPanel")  as IMyTextPanel;
    remote      = GridTerminalSystem.GetBlockWithName("RemoteControl") as IMyRemoteControl;
    
    sensors.Clear();
    GridTerminalSystem.GetBlocksOfType(sensors, s => s.CubeGrid == Me.CubeGrid);
    
    // Enable sensor scanning
    foreach (var s in sensors)
    {
        s.DetectAsteroids = true;
        s.DetectShips     = false;
        s.DetectFloatingObjects = false;
        s.DetectFriendly  = false;
        s.DetectNeutral   = true;
        s.DetectEnemies   = false;
    }
    
    // Activate ore detector
    oreDetector.Enable();
}

//———————— ORIGIN SETUP ——————————
void SetupOrigin()
{
    string data = textPanel.GetPublicText().Trim();
    if (string.IsNullOrEmpty(data))
    {
        origin = Me.CubeGrid.WorldMatrix.Translation;
        textPanel.WritePublicText(origin.ToString("F3"));
    }
    else
    {
        Vector3D.TryParse(data, out origin);
    }
    originLoaded = true;
}

//———————— GENERAL ROUTINE ——————————
void RunGeneralRoutine()
{
    if (laser.CanShoot)
    {
        lastPhoneHomePos = Me.CubeGrid.WorldMatrix.Translation;
    }
}

//———————— SEARCH ROUTINE ——————————
void RunSearchRoutine()
{
    // Step 1: detect nearby asteroids
    foreach (var sensor in sensors)
    {
        var detected = sensor.LastDetectedEntities;
        foreach (var ent in detected)
        {
            if (ent.Type == MyDetectedEntityType.Asteroid && ent.Position.Distance(Me.CubeGrid.WorldMatrix.Translation) <= DETECT_DISTANCE)
            {
                if (!pendingAsteroids.Contains(ent.EntityId))
                {
                    pendingAsteroids.Add(ent.EntityId);
                    surveyAsteroidIdx = pendingAsteroids.Count - 1;
                    currentRoutine = Routine.Survey;
                    surveyStartPos = Me.CubeGrid.WorldMatrix.Translation;
                    return;
                }
            }
        }
    }
    
    // Step 2: generate next waypoint if needed
    if (spiralWaypoints.Count == 0)
        GenerateNextSpiralLeg();
    
    // Step 3: go to next waypoint
    gotoTarget = spiralWaypoints[0];
    spiralWaypoints.RemoveAt(0);
    returnRoutineData = Routine.Search.ToString();
    currentRoutine = Routine.GoTo;
}

// Generate a segment of the square spiral
void GenerateNextSpiralLeg()
{
    Vector3D dir = Vector3D.Zero;
    switch (segmentTurnCount % 4)
    {
        case 0: dir = Vector3D.Right; break;   // +X
        case 1: dir = Vector3D.Forward; break; // +Z
        case 2: dir = Vector3D.Left; break;    // -X
        case 3: dir = Vector3D.Backward; break;// -Z
    }
    
    for (int i = 1; i <= segmentLength / SPIRAL_SPACING; i++)
    {
        var pt = origin + dir * (segmentLength * ((segmentTurnCount / 2) + 1));
        spiralWaypoints.Add(pt);
    }
    
    segmentTurnCount++;
    if (segmentTurnCount % 2 == 0)
        segmentLength += SPIRAL_SPACING;
}

//———————— SURVEY ROUTINE ——————————
void RunSurveyRoutine()
{
    // If starting survey on fresh asteroids
    if (surveyOreData.Count == 0)
    {
        // nothing: orbit entry point saved
    }
    
    var targetEntity = MyAPIGateway.Entities.GetEntityById(pendingAsteroids[surveyAsteroidIdx]) as IMyVoxelMap;
    if (targetEntity == null)
    {
        AdvanceSurveyIndexOrReturn();
        return;
    }
    
    // Orbit equatorial
    OrbitEntity(targetEntity, false);
    // Orbit polar
    OrbitEntity(targetEntity, true);
    
    // Phone home after orbits
    phoneHomeMessage = string.Join(";", surveyOreData);
    phoneHomeChannel = laser.EntityId;
    returnRoutineData = $"{Routine.Search}:{surveyStartPos}";
    currentRoutine = Routine.PhoneHome;
}

// Orbit around the target entity
void OrbitEntity(IMyEntity ent, bool polar)
{
    var center = ent.WorldMatrix.Translation;
    double radius = ent.WorldVolume.Radius + ORBIT_DISTANCE;
    
    // define orbit basis
    Vector3D axisA = polar ? Vector3D.Up : Vector3D.Forward;
    Vector3D axisB = polar ? Vector3D.Right : Vector3D.Up;
    
    for (int i = 0; i < ORBIT_SEGMENTS; i++)
    {
        double theta = 2 * Math.PI * i / ORBIT_SEGMENTS;
        var offset = axisA * Math.Cos(theta) * radius + axisB * Math.Sin(theta) * radius;
        gotoTarget = center + offset;
        returnRoutineData = Routine.Survey.ToString();
        currentRoutine = Routine.GoTo;
        SaveState();               // pause between orbits
        return;                    // wait tick to continue
    }
}

// Once one asteroid is done, advance or finish
void AdvanceSurveyIndexOrReturn()
{
    // record ore (mocked)
    surveyOreData.Add($"Asteroid{pendingAsteroids[surveyAsteroidIdx]}@{Me.CubeGrid.WorldMatrix.Translation:F2}");
    
    surveyAsteroidIdx++;
    if (surveyAsteroidIdx < pendingAsteroids.Count)
    {
        // survey next asteroid
        currentRoutine = Routine.Survey;
    }
    else
    {
        // all done: return home info
        surveyAsteroidIdx = 0;
        pendingAsteroids.Clear();
    }
}

//———————— PHONE HOME ROUTINE ——————————
void RunPhoneHomeRoutine()
{
    // check connection
    if (!laser.CanShoot)
    {
        gotoTarget = lastPhoneHomePos;
        returnRoutineData = Routine.PhoneHome.ToString();
        currentRoutine = Routine.GoTo;
        return;
    }
    
    // broadcast
    laser.EnableBroadcastChannel = true;
    laser.EnableReceiveChannel   = true;
    laser.SetValue("Channel", phoneHomeChannel);
    laser.ShootOnce(phoneHomeMessage);
    
    if (!waitingForAck)
    {
        waitingForAck = true;
        return;
    }
    
    // check for Ack
    string msg;
    while (laser.HasWaitingMessage)
    {
        laser.TryGetNextReceivedMessage(out phoneHomeChannel, out msg);
        if (msg == "Ack")
        {
            waitingForAck = false;
            ParseReturnRoutine();
            return;
        }
    }
}

//———————— GO TO ROUTINE ——————————
void RunGoToRoutine()
{
    // set up autopilot
    remote.ClearWaypoints();
    remote.AddWaypoint(gotoTarget, "Next");
    remote.SetCollisionAvoidance(true);
    remote.SetAutoPilotEnabled(true);
    
    // have we arrived?
    if (remote.IsUnderControl && Vector3D.Distance(Me.CubeGrid.WorldMatrix.Translation, gotoTarget) < 10.0)
    {
        remote.SetAutoPilotEnabled(false);
        ParseReturnRoutine();
    }
}

// Parses returnRoutineData and transitions routines
void ParseReturnRoutine()
{
    var parts = returnRoutineData.Split(':');
    if (!Enum.TryParse(parts[0], out Routine rtn))
        rtn = Routine.Search;
    
    currentRoutine = rtn;
    if (parts.Length > 1 && rtn == Routine.Search)
    {
        Vector3D.TryParse(parts[1], out surveyStartPos);
    }
    returnRoutineData = "";
}

//———————— STATE SAVE/LOAD ——————————
void SaveState()
{
    var sb = new StringBuilder();
    sb.AppendLine($"currentRoutine={(int)currentRoutine}");
    sb.AppendLine($"returnData={returnRoutineData}");
    sb.AppendLine($"origin={origin:F3}");
    sb.AppendLine($"originLoaded={originLoaded}");
    sb.AppendLine($"lastPhone={lastPhoneHomePos:F3}");
    sb.AppendLine($"spiralLayer={spiralLayer}");
    sb.AppendLine($"segmentTurns={segmentTurnCount}");
    sb.AppendLine($"segmentLength={segmentLength}");
    sb.AppendLine($"pendingAsteroids={string.Join(",", pendingAsteroids)}");
    sb.AppendLine($"surveyIdx={surveyAsteroidIdx}");
    sb.AppendLine($"surveyStart={surveyStartPos:F3}");
    sb.AppendLine($"oreData={string.Join("|", surveyOreData)}");
    sb.AppendLine($"phoneMsg={phoneHomeMessage}");
    sb.AppendLine($"phoneChannel={phoneHomeChannel}");
    sb.AppendLine($"waitingAck={waitingForAck}");
    sb.AppendLine($"gotoTarget={gotoTarget:F3}");
    Storage = sb.ToString();
}

void LoadState()
{
    if (string.IsNullOrEmpty(Storage)) return;
    var lines = Storage.Split('\n');
    foreach (var line in lines)
    {
        var kv = line.Trim().Split(new[]{'='},2);
        if (kv.Length < 2) continue;
        var key = kv[0];
        var val = kv[1];
        switch (key)
        {
            case "currentRoutine":    currentRoutine   = (Routine)int.Parse(val); break;
            case "returnData":        returnRoutineData= val; break;
            case "origin":            Vector3D.TryParse(val, out origin); break;
            case "originLoaded":      originLoaded     = bool.Parse(val); break;
            case "lastPhone":         Vector3D.TryParse(val, out lastPhoneHomePos); break;
            case "spiralLayer":       spiralLayer      = int.Parse(val); break;
            case "segmentTurns":      segmentTurnCount = int.Parse(val); break;
            case "segmentLength":     segmentLength    = double.Parse(val); break;
            case "pendingAsteroids":  pendingAsteroids = val.Split(new[]{','}, StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToList(); break;
            case "surveyIdx":         surveyAsteroidIdx= int.Parse(val); break;
            case "surveyStart":       Vector3D.TryParse(val, out surveyStartPos); break;
            case "oreData":           surveyOreData    = val.Split(new[]{"|"}, StringSplitOptions.RemoveEmptyEntries).ToList(); break;
            case "phoneMsg":          phoneHomeMessage = val; break;
            case "phoneChannel":      phoneHomeChannel = long.Parse(val); break;
            case "waitingAck":        waitingForAck    = bool.Parse(val); break;
            case "gotoTarget":        Vector3D.TryParse(val, out gotoTarget); break;
        }
    }
}

//—————— ADDITIONAL REQUIRED BLOCKS ——————
// - Remote Control (named "RemoteControl") for autopilot & collision avoidance
// - Sensor Blocks (any name) with DetectAsteroids = true
// - Gyroscopes & Thrusters properly configured on the grid
// - (Optional) Cameras if you prefer raycasting vs sensors
