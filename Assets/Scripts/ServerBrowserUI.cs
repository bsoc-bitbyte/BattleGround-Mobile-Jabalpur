using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Photon.Pun;
using Photon.Realtime;

public class ServerBrowserUI : MonoBehaviourPunCallbacks
{
    [Header("Setup")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private VisualTreeAsset roomRowTemplate;
    [SerializeField] private RoomManager roomManager; // Link your RoomManager here!

    private ScrollView roomScrollView;
    private Button createRoomButton;
    private TextField roomNameInput;

    public override void OnEnable()
    {
        // 1. Tell Photon to run its background setup first!
        base.OnEnable();

        // 2. Now run our UI Toolkit setup
        VisualElement root = uiDocument.rootVisualElement;

        roomScrollView = root.Q<ScrollView>("RoomScrollView");
        createRoomButton = root.Q<Button>("CreateButton");
        roomNameInput = root.Q<TextField>("RoomNameInput");

        if (createRoomButton != null)
        {
            createRoomButton.clicked += () =>
            {
                if (!string.IsNullOrEmpty(roomNameInput.value))
                {
                    roomManager.CreateCustomRoom(roomNameInput.value);
                }
            };
        }
    }

    // It is also best practice to add OnDisable to clean things up
    public override void OnDisable()
    {
        // Tell Photon to stop sending updates when this menu is turned off
        base.OnDisable();
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        if (roomScrollView == null) return;
        roomScrollView.Clear();

        foreach (RoomInfo room in roomList)
        {
            if (room.RemovedFromList || !room.IsOpen || !room.IsVisible) continue;

            VisualElement newRow = roomRowTemplate.Instantiate();
            newRow.Q<Label>("RoomName").text = room.Name;
            newRow.Q<Label>("PlayerCount").text = $"{room.PlayerCount}/{room.MaxPlayers}";

            newRow.Q<Button>("JoinButton").clicked += () =>
            {
                PhotonNetwork.JoinRoom(room.Name);
            };

            roomScrollView.Add(newRow);
        }
    }

    // Automatically hide the entire UI overlay once the Room Manager successfully hooks into a match
    public override void OnJoinedRoom()
    {
        uiDocument.rootVisualElement.style.display = DisplayStyle.None;
    }
}