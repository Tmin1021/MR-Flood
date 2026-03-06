# MR Flood Navigation

An interactive Mixed Reality prototype built in Unity for exploring **flood-aware navigation** on a tabletop city model. Users can select buildings in the city, simulate rising water levels, and visualize a safe route between locations based on a graph of road nodes.

This project is part of a broader VR/AR/MR human interaction study focused on **disaster-awareness and navigation support** in constrained environments.

## Overview

The prototype turns a 3D city model into an interactive MR map. It supports:

- Selecting a **start** and **destination** building
- Simulating **flood level changes** with an MRTK slider
- Blocking flooded nodes dynamically
- Computing a route with **A\*** pathfinding
- Rendering the route as a visual path in the scene
- Providing lightweight MR notifications when a route is unavailable

The current project is designed around a **miniature city / tabletop model** interaction style, where users inspect and manipulate the scene directly in Mixed Reality.

## Main Features

### 1. Building selection
Users can focus or click buildings in the city model to choose origin and destination points.

### 2. Flood-aware routing
A graph of road/intersection nodes is built from scene objects. When the water level rises, nodes below the threshold are marked as blocked. The pathfinder then searches only through available nodes.

### 3. Interactive flood simulation
A slider controls the water level in the scene. This allows testing how route availability changes under different flood conditions.

### 4. Path visualization
A `LineRenderer` is used to show a route from the selected building, through the graph nodes, to the destination building.

### 5. MR UI feedback
Short notifications inform the user when a building is flooded, no route exists, or the graph has missing nodes.

## Core Scripts

### `PointSelectManager.cs`
Handles building selection, hover tag display, confirm/reset logic, nearest-node lookup, A\* route requests, and path rendering.

### `AStarPathFinder.cs`
Implements the A\* pathfinding algorithm over graph nodes.

### `SimpleGraphManager.cs`
Builds the node graph from scene objects, adds edges from neighbor assignments, and updates node blocking based on flood level.

### `GraphNode.cs`
Represents a graph node and stores its connected edges, block state, and effective height.

### `NodeNeighbors.cs`
Stores manual graph neighbor relationships for each node.

### `BuildingPoint.cs`
Adds interactable behavior to building objects and reports focus / click events back to the selection manager.

### `WaterLevelController.cs`
Drives flood height changes from an MRTK slider.

### `MRNotification.cs`
Displays short floating text feedback in MR.

### `EventManager.cs`
Contains utility toggles such as water-plane visibility, OSM/Bing material switching, and bounds control for the city model.

## Project Workflow

1. Load the city model into the scene.
2. Assign buildings under a common parent object.
3. Place graph nodes along roads / intersections.
4. Define node neighbors using `NodeNeighbors`.
5. Select two buildings in MR.
6. Press the confirm button.
7. Generate and display the safest route under the current flood level.

## Scene / Interaction Logic

### Building interaction
- Buildings must have a **Collider** to be selectable.
- `PointSelectManager` automatically adds `BuildingPoint` and a `BoxCollider` to valid building objects if missing.

### Graph routing
- Each node is a `GraphNode`.
- Neighbor relationships are defined through `NodeNeighbors`.
- `SimpleGraphManager` converts those relationships into weighted graph edges.
- A\* finds the shortest valid path while ignoring blocked nodes / edges.

### Flood blocking
- Each node is blocked if its effective height is below the current water level.
- Buildings are also checked before routing to avoid selecting flooded locations.

## Requirements

- **Unity 2023.2.x**
- **Universal Windows Platform (UWP)** target if deploying to HoloLens
- **MRTK 2.8.3**

## Recommended Project Setup

### Scene references
Assign the following references in the Inspector:

#### `PointSelectManager`
- `path`: LineRenderer used to draw the route
- `housesParent`: parent of building objects
- `hoverTag`: object used as the building hover label
- `confirmButton`: button to confirm route generation
- `resetButton`: button to clear current selections
- `hoverText`: UI text shown above hovered building
- `graph`: reference to `SimpleGraphManager`
- `notifier`: reference to `MRNotification`

#### `SimpleGraphManager`
- `nodesParent`: parent containing all graph nodes
- `waterLevel`: initial flood level
- `autoUpdateFlood`: whether to re-evaluate flooded nodes every frame

#### `WaterLevelController`
- `slider`: MRTK `PinchSlider`
- `waterPlane`: transform of the flood plane
- `city`: root city transform

## How to Use

1. Start the scene.
2. Hover over or select a building.
3. Select the first building as the start point.
4. Select a second building as the destination.
5. Press the confirm button.
6. The system finds the closest graph nodes and computes a route.
7. Adjust the water slider to simulate flooding and test whether the route remains available.

## Current Limitations

- Building-to-node connection currently uses the **closest graph node**, which may not always match the exact road entrance.
- Route rendering uses a `LineRenderer`, which can be sensitive to width, depth, and visibility settings on MR devices.
- Near interaction / poke-based selection may require additional MRTK configuration depending on the device and interaction profile.
- Flood logic currently uses node height thresholds rather than full terrain-based water simulation.

## Known Rendering Note

If the route line is hard to see on device:

- keep the line slightly above the road surface
- use a small but visible width multiplier
- use a high-contrast unlit material
- verify LineRenderer alignment and depth behavior
- test on the actual target device, because editor rendering may differ from MR capture

## Suggested Future Improvements

- Replace closest-node routing with explicit building entrance nodes
- Generate a flatter road-overlay mesh instead of a `LineRenderer`
- Add dynamic hazard zones such as landslides or blocked streets
- Support near-touch / poke interaction for easier building selection
- Add tabletop scaling, filtering, and route explanation UI
- Extend the system for smoke, dust, or low-visibility emergency navigation scenarios

## Research Context

This prototype explores how MR can support **human interaction with environmental hazards** through intuitive spatial visualization. Instead of viewing a 2D map, users interact directly with a 3D city representation and observe how route availability changes as flood conditions evolve.

This concept can support future work in:

- MR disaster education
- urban resilience visualization
- emergency route planning
- interactive tabletop city interfaces

## Credits

Developed as an MR prototype in Unity using MRTK.

## License

