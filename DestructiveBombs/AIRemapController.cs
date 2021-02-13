using RWCustom;
using UnityEngine;
using System.Collections.Generic;
using System;

class AIRemapController : MonoBehaviour
{
    public static AIRemapController main;
    public static GameObject go;

    public Dictionary<Room, AImapper> mappers = new Dictionary<Room, AImapper>();

    public AIRemapController()
    {
        main = this;
    }
    
    public void StopMappingRoom(Room room)
    {
        if (mappers.ContainsKey(room))
        {
            Debug.Log("Aborted mapping " + room.abstractRoom.name);
            mappers.Remove(room);
        }
    }

    public void StartMappingRoom(Room room)
    {
        Debug.Log("Started mapping " + room.abstractRoom.name);
        mappers[room] = new AImapper(room);
    }

    const int stepsPerUpdate = 100;
    private List<Room> _roomsToRemove = new List<Room>();
    public void FixedUpdate()
    {
        if (_roomsToRemove.Count > 0) _roomsToRemove.Clear();
        foreach(KeyValuePair<Room, AImapper> pair in mappers)
        {
            for (int step = 0; step < 300; step++)
            {
                pair.Value.Update();
                if(pair.Value.done)
                {
                    Debug.Log("Finished mapping room " + pair.Key.abstractRoom.name);
                    _roomsToRemove.Add(pair.Key);
                    AImap map = pair.Value.ReturnAIMap();
                    map.creatureSpecificAImaps = pair.Key.aimap.creatureSpecificAImaps;
                    pair.Key.aimap = map;
                    break;
                }
            }
        }
        foreach(Room room in _roomsToRemove)
            mappers.Remove(room);
    }
}
