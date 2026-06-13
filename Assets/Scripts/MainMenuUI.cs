using UnityEngine;
using UnityEngine.UIElements;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

/// <summary>
/// Self-contained Main Menu.
/// Handles its OWN Photon connection - does NOT depend on RoomManager.
/// RoomManager only handles spawning the player AFTER the room is joined.
///
/// NEW FEATURES:
///  - Timings section: host can select match duration (1 / 3 / 5 / 10 min) before creating room.
///  - Selected duration is stored in Room Custom Properties ("matchTime") so TeamManager reads it.
///  - ShowMenu() public method re-displays the UI after a match ends (no scene reload needed).
///  - Instance singleton so GameHUDController can call ShowMenu() after match over.
/// </summary>
public class MainMenuUI : MonoBehaviourPunCallbacks
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static MainMenuUI Instance { get; private set; }

    private UIDocument uiDocument;
    private VisualElement root;

    private Button    hostButton;
    private Button    joinButton;
    private TextField codeInput;
    private Label     statusLabel;
    // New UXML structure: RoomCodeDisplay is the outer box (VisualElement),
    // RoomCodeValue is the inner Label that shows the actual code string.
    private VisualElement roomCodeBox;
    private Label         roomCodeValueLabel;

    // ── Timing selector ───────────────────────────────────────────────────────
    private Button    time1MinBtn;
    private Button    time3MinBtn;
    private Button    time5MinBtn;
    private Button    time10MinBtn;
    private int       selectedMatchSeconds = 300; // default 5 min
    private Label     selectedTimeLabel;

    // ── Unity Lifecycle ───────────────────────────────────────────────────────
    private void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("[MainMenuUI] No UIDocument on this GameObject!");
            return;
        }

        root = uiDocument.rootVisualElement;

        hostButton       = root.Q<Button>("HostButton");
        joinButton       = root.Q<Button>("JoinButton");
        codeInput        = root.Q<TextField>("CodeInput");
        statusLabel      = root.Q<Label>("StatusLabel");
        roomCodeBox      = root.Q<VisualElement>("RoomCodeDisplay");
        roomCodeValueLabel = root.Q<Label>("RoomCodeValue");

        // Timing buttons
        time1MinBtn   = root.Q<Button>("Time1Min");
        time3MinBtn   = root.Q<Button>("Time3Min");
        time5MinBtn   = root.Q<Button>("Time5Min");
        time10MinBtn  = root.Q<Button>("Time10Min");
        selectedTimeLabel = root.Q<Label>("SelectedTimeLabel");

        if (hostButton == null) Debug.LogError("[MainMenuUI] HostButton not found in UXML!");
        if (joinButton == null) Debug.LogError("[MainMenuUI] JoinButton not found in UXML!");

        // Buttons disabled until we are in the lobby
        SetButtonsEnabled(false);

        if (hostButton != null) hostButton.clicked += OnHostClicked;
        if (joinButton != null) joinButton.clicked += OnJoinClicked;

        // Wire timing buttons
        if (time1MinBtn  != null) time1MinBtn.clicked  += () => SelectTime(60);
        if (time3MinBtn  != null) time3MinBtn.clicked  += () => SelectTime(180);
        if (time5MinBtn  != null) time5MinBtn.clicked  += () => SelectTime(300);
        if (time10MinBtn != null) time10MinBtn.clicked += () => SelectTime(600);

        // Highlight default selection (5 min)
        SelectTime(300);

        SetStatus("Connecting to server...");
    }

    private void Start()
    {
        // This script handles its own connection
        if (!PhotonNetwork.IsConnected)
        {
            Debug.Log("[MainMenuUI] Connecting to Photon...");
            PhotonNetwork.ConnectUsingSettings();
        }
        else if (PhotonNetwork.InLobby)
        {
            // Already connected and in lobby (e.g. returning to menu)
            OnJoinedLobby();
        }
        else if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.JoinLobby();
        }
    }

    // ── Photon Callbacks ──────────────────────────────────────────────────────
    public override void OnConnectedToMaster()
    {
        Debug.Log("[MainMenuUI] Connected to Master. Joining lobby...");
        SetStatus("Connected! Joining lobby...");
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        Debug.Log("[MainMenuUI] Joined Lobby. Buttons enabled.");
        SetStatus("Ready! Host a game or enter a code to join.");
        SetButtonsEnabled(true);
    }

    public override void OnCreatedRoom()
    {
        string code = PhotonNetwork.CurrentRoom.Name;
        Debug.Log("[MainMenuUI] Room created: " + code);
        int mins = selectedMatchSeconds / 60;
        SetStatus($"Room created!  Share this code  ·  {mins} min match");

        if (roomCodeBox != null)
            roomCodeBox.style.display = DisplayStyle.Flex;
        if (roomCodeValueLabel != null)
            roomCodeValueLabel.text = code;
    }

    public override void OnJoinedRoom()
    {
        Debug.Log("[MainMenuUI] Joined room: " + PhotonNetwork.CurrentRoom.Name);
        // Hide the menu — game starts
        if (root != null)
            root.style.display = DisplayStyle.None;
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError("[MainMenuUI] Create room failed: " + message);
        SetStatus("Failed to create room: " + message);
        SetButtonsEnabled(true);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError("[MainMenuUI] Join room failed: " + message);
        SetStatus("Failed to join: " + message + " — check the code.");
        SetButtonsEnabled(true);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning("[MainMenuUI] Disconnected: " + cause);
        SetStatus("Disconnected: " + cause + ". Reconnecting...");
        SetButtonsEnabled(false);
        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnLeftRoom()
    {
        // After leaving a room (e.g. match ended), go back to lobby + show menu
        Debug.Log("[MainMenuUI] Left room. Returning to lobby...");
        PhotonNetwork.JoinLobby();
        ShowMenu();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Re-displays the main menu. Called after match ends.</summary>
    public void ShowMenu()
    {
        if (root != null)
            root.style.display = DisplayStyle.Flex;

        // Reset room code box
        if (roomCodeBox != null)
            roomCodeBox.style.display = DisplayStyle.None;
        if (roomCodeValueLabel != null)
            roomCodeValueLabel.text = "";

        SetStatus("Ready! Host a game or enter a code to join.");
        SetButtonsEnabled(true);
    }

    // ── Button Handlers ───────────────────────────────────────────────────────
    private void OnHostClicked()
    {
        string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        string code  = "";
        for (int i = 0; i < 6; i++)
            code += chars[Random.Range(0, chars.Length)];

        Debug.Log("[MainMenuUI] Creating room: " + code + " matchTime=" + selectedMatchSeconds);
        SetStatus("Creating room " + code + "...");
        SetButtonsEnabled(false);

        // Store match duration in Room Custom Properties so TeamManager reads it
        var customProps = new Hashtable { { TeamManager.PropMatchTime, selectedMatchSeconds } };

        RoomOptions options = new RoomOptions
        {
            MaxPlayers = 10,
            IsOpen     = true,
            IsVisible  = true,
            CustomRoomProperties            = customProps,
            CustomRoomPropertiesForLobby    = new[] { TeamManager.PropMatchTime }
        };
        PhotonNetwork.CreateRoom(code, options);
    }

    private void OnJoinClicked()
    {
        if (codeInput == null) return;

        string code = codeInput.text.Trim().ToUpper();
        if (code.Length != 6)
        {
            SetStatus("Please enter a 6-character room code.");
            return;
        }

        Debug.Log("[MainMenuUI] Joining room: " + code);
        SetStatus("Joining room " + code + "...");
        SetButtonsEnabled(false);
        PhotonNetwork.JoinRoom(code);
    }

    // ── Timing helpers ────────────────────────────────────────────────────────
    private void SelectTime(int seconds)
    {
        selectedMatchSeconds = seconds;

        // Visual feedback: add/remove "selected" class on buttons
        UpdateTimingButtonStyles(seconds);

        int mins = seconds / 60;
        string label = mins == 1 ? "1 minute" : $"{mins} minutes";
        if (selectedTimeLabel != null)
            selectedTimeLabel.text = $"Selected  \u203a  {label}";
    }

    private void UpdateTimingButtonStyles(int selectedSeconds)
    {
        SetTimingButtonSelected(time1MinBtn,  selectedSeconds == 60);
        SetTimingButtonSelected(time3MinBtn,  selectedSeconds == 180);
        SetTimingButtonSelected(time5MinBtn,  selectedSeconds == 300);
        SetTimingButtonSelected(time10MinBtn, selectedSeconds == 600);
    }

    private void SetTimingButtonSelected(Button btn, bool selected)
    {
        if (btn == null) return;
        if (selected)
            btn.AddToClassList("time-btn-selected");
        else
            btn.RemoveFromClassList("time-btn-selected");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void SetStatus(string message)
    {
        if (statusLabel != null)
            statusLabel.text = message;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        if (hostButton != null) hostButton.SetEnabled(enabled);
        if (joinButton != null) joinButton.SetEnabled(enabled);
    }
}
