# WRB Unity Editor Utils

**Unity Editor Tools Collection**  
📦 `com.wrbstudio.unity.editorutils`  
A modular collection of useful tools for Unity Editor workflows.

---


## 🔧 Features

- **FBX Prefab Batcher**  
  Quickly convert FBX models (single or multiple) into ready-to-use prefabs.  
  - Extracts materials and textures  
  - Optionally adds colliders and scripts  
  - Supports models with multiple mesh parts  

---


## 📦 Installation

### Option 1: Using Unity Package Manager (Recommended)

1. In Unity, open **Window → Package Manager**
2. Click the **"+"** button in the top-left corner
3. Select **"Add package from Git URL..."**
4. Paste the following URL: "https://github.com/WRB-Studio/com.wrbstudio.unity.editorutils.git"
5. Click **"Add"**


## 🛠️ Usage

1. Open the **FBX Prefab Batcher** via `Tools → FBX Prefab Batcher`
2. Set the **Input** to an FBX file or a folder with FBX models
3. Set the **Output** to a folder inside your project where prefabs should be saved
4. Configure settings:
   - Tag to assign
   - Collider type (Box, Mesh, etc.)
   - Optional script to attach
   - Enable material extraction (optional)
   - Enable shared material cache (optional)
5. Click **"Generate Prefabs"**  
→ Unity will create prefabs and organize assets accordingly.

---


##TODO:

- collider only if mesh exist
- checkbox for add collider to childs

---
