using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using DFHack;
using RemoteFortressReader;
using UnityEngine.UI;
using System.IO;
using UnityExtension;

public class GameMap : MonoBehaviour
{
    public ContentLoader contentLoader = new ContentLoader();
    static readonly int l_water = 0;
    static readonly int l_magma = 1;
    MapTile[, ,] tiles;
    Light[, ,] magmaGlow;
    public Light magmaGlowPrefab;
    //public GenericTile tileSelector;

    public Material basicTerrainMaterial;
    public Material stencilTerrainMaterial;
    public Material waterMaterial;
    public Material magmaMaterial;
    public Material invisibleMaterial;
    public Material invisibleStencilMaterial;

    public ConnectionState connectionState;
    public GameObject defaultMapBlock;
    public GameObject defaultStencilMapBLock;
    public GameObject defaultWaterBlock;
    public GameObject defaultMagmaBlock;
    public GameWindow viewCamera;
    public MeshFilter[, ,] blocks; // Dumb blocks for holding the terrain data.
    public MeshFilter[, ,] stencilBlocks;
    public MeshFilter[, , ,] liquidBlocks; // Dumb blocks for holding the water.
    public bool[, ,] blockDirtyBits;
    public bool[, ,] waterBlockDirtyBits; //also for magma
    public int rangeX = 0;
    public int rangeY = 0;
    public int rangeZup = 0;
    public int rangeZdown = 0;
    public int blocksToGet = 1;
    public int posX = 0;
    public int posY = 0;
    public int posZ = 0;
    public int map_x;
    public int map_y;
    public Text genStatus;
    public Text cursorProperties;

    Dictionary<MatPairStruct, RemoteFortressReader.MaterialDefinition> materials;

    public static float tileHeight { get { return 3.0f; } }
    public static float floorHeight { get { return 0.5f; } }
    public static float tileWidth { get { return 2.0f; } }
    public static int blockSize = 16;
    public static Vector3 DFtoUnityCoord(int x, int y, int z)
    {
        Vector3 outCoord = new Vector3(x * tileWidth, z * tileHeight, y * (-tileWidth));
        return outCoord;
    }
    public static Vector3 DFtoUnityCoord(DFCoord input)
    {
        Vector3 outCoord = new Vector3(input.x * tileWidth, input.z * tileHeight, input.y * (-tileWidth));
        return outCoord;
    }
    MeshCombineUtility.MeshInstance[] meshBuffer;
    MeshCombineUtility.MeshInstance[] stencilMeshBuffer;
    //CombineInstance[] meshBuffer;

    System.Diagnostics.Stopwatch blockListTimer = new System.Diagnostics.Stopwatch();
    System.Diagnostics.Stopwatch cullTimer = new System.Diagnostics.Stopwatch();
    System.Diagnostics.Stopwatch lazyLoadTimer = new System.Diagnostics.Stopwatch();

    // Use this for initialization
    void Start()
    {
        Connect();
        InitializeBlocks();
        //connectionState.HashCheckCall.execute();
        GetViewInfo();
        PositionCamera();
        GetMaterialList();
        GetTiletypeList();
        GetUnitList();
        //GetBlockList();
        //Disconnect();
        System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        watch.Start();
        MaterialTokenList.matTokenList = connectionState.net_material_list.material_list;
        TiletypeTokenList.tiletypeTokenList = connectionState.net_tiletype_list.tiletype_list;
        MapTile.tiletypeTokenList = connectionState.net_tiletype_list.tiletype_list;
        contentLoader.ParseContentIndexFile(Application.streamingAssetsPath + "/index.txt");
        watch.Stop();
        Debug.Log("Took a total of " + watch.ElapsedMilliseconds + "ms to load all XML files.");
        connectionState.MapResetCall.execute();
        blockListTimer.Start();
        cullTimer.Start();
        lazyLoadTimer.Start();
        //InvokeRepeating("GetBlockList", 0, 0.1f);
        //InvokeRepeating("CullDistantBlocks", 1, 2);
        //InvokeRepeating("LazyLoadBlocks", 1, 1);
    }

    public void ConnectToDF()
    {
        if (connectionState != null)
            return;

    }

    bool gotBlocks = false;
    // Update is called once per frame
    void Update()
    {
        connectionState.network_client.suspend_game();
        GetViewInfo();
        PositionCamera();
        ShowCursorInfo();
        //if (blockListTimer.ElapsedMilliseconds > 30)
        {
            GetBlockList();
            blockListTimer.Reset();
            blockListTimer.Start();
            gotBlocks = true;
        }
        //if (lazyLoadTimer.ElapsedMilliseconds > 1000)
        //{
        //    LazyLoadBlocks();
        //    lazyLoadTimer.Reset();
        //    lazyLoadTimer.Start();
        //}
        GetUnitList();
        connectionState.network_client.resume_game();
        //UpdateCreatures();
        if(gotBlocks)
        {
            UseBlockList();
            gotBlocks = false;
        }
        if (cullTimer.ElapsedMilliseconds > 100)
        {
            CullDistantBlocks();
            cullTimer.Reset();
            cullTimer.Start();
        }
        HideMeshes();
    }

    void OnDestroy()
    {
        Disconnect();
    }

    void InitializeBlocks()
    {
        GetMapInfo();
        Debug.Log("Map Size: " + connectionState.net_map_info.block_size_x + ", " + connectionState.net_map_info.block_size_y + ", " + connectionState.net_map_info.block_size_z);
        tiles = new MapTile[connectionState.net_map_info.block_size_x * 16, connectionState.net_map_info.block_size_y * 16, connectionState.net_map_info.block_size_z];
        blocks = new MeshFilter[connectionState.net_map_info.block_size_x * 16 / blockSize, connectionState.net_map_info.block_size_y * 16 / blockSize, connectionState.net_map_info.block_size_z];
        stencilBlocks = new MeshFilter[connectionState.net_map_info.block_size_x * 16 / blockSize, connectionState.net_map_info.block_size_y * 16 / blockSize, connectionState.net_map_info.block_size_z];
        liquidBlocks = new MeshFilter[connectionState.net_map_info.block_size_x * 16 / blockSize, connectionState.net_map_info.block_size_y * 16 / blockSize, connectionState.net_map_info.block_size_z, 2];
        blockDirtyBits = new bool[connectionState.net_map_info.block_size_x * 16 / blockSize, connectionState.net_map_info.block_size_y * 16 / blockSize, connectionState.net_map_info.block_size_z];
        waterBlockDirtyBits = new bool[connectionState.net_map_info.block_size_x * 16 / blockSize, connectionState.net_map_info.block_size_y * 16 / blockSize, connectionState.net_map_info.block_size_z];
        magmaGlow = new Light[connectionState.net_map_info.block_size_x * 16, connectionState.net_map_info.block_size_y * 16, connectionState.net_map_info.block_size_z];
    }

    void SetDirtyBlock(int mapBlockX, int mapBlockY, int mapBlockZ)
    {
        mapBlockX = mapBlockX / blockSize;
        mapBlockY = mapBlockY / blockSize;
        blockDirtyBits[mapBlockX, mapBlockY, mapBlockZ] = true;
    }
    void SetDirtyWaterBlock(int mapBlockX, int mapBlockY, int mapBlockZ)
    {
        mapBlockX = mapBlockX / blockSize;
        mapBlockY = mapBlockY / blockSize;
        waterBlockDirtyBits[mapBlockX, mapBlockY, mapBlockZ] = true;
    }

    //void InitializeBlocks()
    //{
    //    if (blockCollection == null)
    //        blockCollection = new List<MapBlock>();
    //    int wantedSize = rangeX * 2 * rangeY * 2 * (rangeZup + rangeZdown);
    //    if (blockCollection.Count < wantedSize)
    //        for (int i = blockCollection.Count; i < wantedSize; i++)
    //        {
    //            MapBlock newblock = Instantiate(defaultMapBlock) as MapBlock;
    //            newblock.transform.parent = this.transform;
    //            newblock.parent = this;
    //            blockCollection.Add(newblock);
    //        }
    //    else if (blockCollection.Count > wantedSize) //This shouldn't happen normally, but better to be prepared than not
    //        for (int i = blockCollection.Count - 1; i >= wantedSize; i--)
    //        {
    //            Destroy(blockCollection[i]);
    //            blockCollection.RemoveAt(i);
    //        }
    //}

    //void FreeAllBlocks()
    //{
    //    foreach(MapBlock block in blockCollection)
    //    {
    //        block.gameObject.SetActive(false);
    //    }
    //}

    void Connect()
    {
        if (connectionState != null)
            return;
        else
        {
            connectionState = new ConnectionState();
            if (!connectionState.is_connected)
                Disconnect();
        }
    }

    void Disconnect()
    {
        connectionState.Disconnect();
        connectionState = null;
    }

    void GetMaterialList()
    {
        System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();
        connectionState.MaterialListCall.execute(null, out connectionState.net_material_list);
        if (materials == null)
            materials = new Dictionary<MatPairStruct, RemoteFortressReader.MaterialDefinition>();
        materials.Clear();
        foreach (RemoteFortressReader.MaterialDefinition material in connectionState.net_material_list.material_list)
        {
            materials[material.mat_pair] = material;
        }
        stopwatch.Stop();
        Debug.Log(materials.Count + " materials gotten, took " + stopwatch.Elapsed.Milliseconds + " ms.");
    }

    void PrintFullMaterialList()
    {
        int limit = connectionState.net_material_list.material_list.Count;
        if (limit >= 100)
            limit = 100;
        //Don't ever do this.
        for (int i = connectionState.net_material_list.material_list.Count - limit; i < connectionState.net_material_list.material_list.Count; i++)
        {
            //no really, don't.
            RemoteFortressReader.MaterialDefinition material = connectionState.net_material_list.material_list[i];
            Debug.Log("{" + material.mat_pair.mat_index + "," + material.mat_pair.mat_type + "}, " + material.id + ", " + material.name);
        }
    }

    //MapBlock getFreeBlock()
    //{
    //    for (int i = 0; i < blockCollection.Count; i++)
    //    {
    //        if (blockCollection[i].gameObject.activeSelf == false)
    //            return blockCollection[i];
    //    }
    //    return null;
    //}

    public MapTile GetTile(int x, int y, int z)
    {
        return tiles[x, y, z];
    }

    void GetTiletypeList()
    {
        System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();
        connectionState.TiletypeListCall.execute(null, out connectionState.net_tiletype_list);
        stopwatch.Stop();
        Debug.Log(connectionState.net_tiletype_list.tiletype_list.Count + " tiletypes gotten, took " + stopwatch.Elapsed.Milliseconds + " ms.");
        SaveTileTypeList();
    }

    void SaveTileTypeList()
    {
        try
        {
            File.Delete("TiletypeList.csv");
        }
        catch (IOException)
        {
            return;
        }
        using (StreamWriter writer = new StreamWriter("TiletypeList.csv"))
        {
            foreach (Tiletype item in connectionState.net_tiletype_list.tiletype_list)
            {
                writer.WriteLine(
                    item.name + "," +
                    item.shape + ":" +
                    item.special + ":" +
                    item.material + ":" +
                    item.variant + ":" +
                    item.direction
                    );
            }
        }
    }

    void CopyTiles(RemoteFortressReader.MapBlock DFBlock)
    {
        for (int xx = 0; xx < 16; xx++)
            for (int yy = 0; yy < 16; yy++)
            {
                if (tiles[DFBlock.map_x + xx, DFBlock.map_y + yy, DFBlock.map_z] == null)
                {
                    tiles[DFBlock.map_x + xx, DFBlock.map_y + yy, DFBlock.map_z] = new MapTile();
                    tiles[DFBlock.map_x + xx, DFBlock.map_y + yy, DFBlock.map_z].position = new DFCoord(DFBlock.map_x + xx, DFBlock.map_y + yy, DFBlock.map_z);
                    tiles[DFBlock.map_x + xx, DFBlock.map_y + yy, DFBlock.map_z].container = tiles;
                }
            }
        if (DFBlock.tiles.Count > 0)
        {
            for (int xx = 0; xx < 16; xx++)
                for (int yy = 0; yy < 16; yy++)
                {
                    MapTile tile = tiles[DFBlock.map_x + xx, DFBlock.map_y + yy, DFBlock.map_z];
                    tile.tileType = DFBlock.tiles[xx + (yy * 16)];
                    tile.material = DFBlock.materials[xx + (yy * 16)];
                    tile.base_material = DFBlock.base_materials[xx + (yy * 16)];
                    tile.layer_material = DFBlock.layer_materials[xx + (yy * 16)];
                    tile.vein_material = DFBlock.vein_materials[xx + (yy * 16)];
                }
            SetDirtyBlock(DFBlock.map_x, DFBlock.map_y, DFBlock.map_z);
        }
        if (DFBlock.water.Count > 0)
        {
            for (int xx = 0; xx < 16; xx++)
                for (int yy = 0; yy < 16; yy++)
                {
                    MapTile tile = tiles[DFBlock.map_x + xx, DFBlock.map_y + yy, DFBlock.map_z];
                    tile.liquid[l_water] = DFBlock.water[xx + (yy * 16)];
                    tile.liquid[l_magma] = DFBlock.magma[xx + (yy * 16)];
                }
            SetDirtyWaterBlock(DFBlock.map_x, DFBlock.map_y, DFBlock.map_z);
        }
    }
    bool GenerateLiquids(int block_x, int block_y, int block_z)
    {
        if (!waterBlockDirtyBits[block_x, block_y, block_z])
            return true;
        waterBlockDirtyBits[block_x, block_y, block_z] = false;
        GenerateLiquidSurface(block_x, block_y, block_z, l_water);
        GenerateLiquidSurface(block_x, block_y, block_z, l_magma);
        return true;
    }

    void FillMeshBuffer(out MeshCombineUtility.MeshInstance buffer, MeshLayer layer, MapTile tile)
    {
        buffer = new MeshCombineUtility.MeshInstance();
        MeshContent content = null;
        if (!contentLoader.tileMeshConfiguration.GetValue(tile, layer, out content))
        {
            buffer.mesh = null;
            return;
        }
        buffer.mesh = content.mesh[(int)layer];
        buffer.transform = Matrix4x4.TRS(DFtoUnityCoord(tile.position), Quaternion.identity, Vector3.one);
        if (tile != null)
        {
            int tileTexIndex = 0;
            IndexContent tileTexContent;
            if (contentLoader.tileTextureConfiguration.GetValue(tile, layer, out tileTexContent))
                tileTexIndex = tileTexContent.value;
            int matTexIndex = 0;
            IndexContent matTexContent;
            if (contentLoader.materialTextureConfiguration.GetValue(tile, layer, out matTexContent))
                matTexIndex = matTexContent.value;
            ColorContent newColorContent;
            Color newColor;
            if (contentLoader.colorConfiguration.GetValue(tile, layer, out newColorContent))
            {
                newColor = newColorContent.value;
            }
            else
            {
                MatPairStruct mat;
                mat.mat_type = -1;
                mat.mat_index = -1;
                switch (layer)
                {
                    case MeshLayer.StaticMaterial:
                    case MeshLayer.StaticCutout:
                        mat = tile.material;
                        break;
                    case MeshLayer.BaseMaterial:
                    case MeshLayer.BaseCutout:
                        mat = tile.base_material;
                        break;
                    case MeshLayer.LayerMaterial:
                    case MeshLayer.LayerCutout:
                        mat = tile.layer_material;
                        break;
                    case MeshLayer.VeinMaterial:
                    case MeshLayer.VeinCutout:
                        mat = tile.vein_material;
                        break;
                    case MeshLayer.NoMaterial:
                    case MeshLayer.NoMaterialCutout:
                        break;
                    case MeshLayer.Growth0Cutout:
                        break;
                    case MeshLayer.Growth1Cutout:
                        break;
                    case MeshLayer.Growth2Cutout:
                        break;
                    case MeshLayer.Growth3Cutout:
                        break;
                    default:
                        break;
                }
                MaterialDefinition mattie;
                if (materials.TryGetValue(mat, out mattie))
                {

                    ColorDefinition color = mattie.state_color;
                    if (color == null)
                        newColor = Color.cyan;
                    else
                        newColor = new Color(color.red / 255.0f, color.green / 255.0f, color.blue / 255.0f, 1);
                }
                else
                {
                    newColor = Color.white;
                }
            }
            buffer.color = newColor;
            buffer.uv1Index = matTexIndex;
            buffer.uv2Index = tileTexIndex;
        }
    }

    bool GenerateTiles(int block_x, int block_y, int block_z)
    {
        if (!blockDirtyBits[block_x, block_y, block_z])
            return true;
        blockDirtyBits[block_x, block_y, block_z] = false;
        int bufferIndex = 0;
        int stencilBufferIndex = 0;
        for (int xx = (block_x * blockSize); xx < (block_x + 1) * blockSize; xx++)
            for (int yy = (block_y * blockSize); yy < (block_y + 1) * blockSize; yy++)
            {
                ////do lights first
                //if (tiles[xx, yy, block_z] != null)
                //{
                //    //do magma lights
                //    if ((xx % 1 == 0) && (yy % 1 == 0) && (block_z % 1 == 0))
                //        if (tiles[xx, yy, block_z].magma > 0 && magmaGlow[xx, yy, block_z] == null)
                //        {
                //            magmaGlow[xx, yy, block_z] = (Light)Instantiate(magmaGlowPrefab);
                //            magmaGlow[xx, yy, block_z].gameObject.SetActive(true);
                //            magmaGlow[xx, yy, block_z].transform.parent = this.transform;
                //            magmaGlow[xx, yy, block_z].transform.position = DFtoUnityCoord(xx, yy, block_z + 1);
                //        }
                //}

                if (meshBuffer == null)
                    meshBuffer = new MeshCombineUtility.MeshInstance[blockSize * blockSize * (int)MeshLayer.StaticCutout];
                if (stencilMeshBuffer == null)
                    stencilMeshBuffer = new MeshCombineUtility.MeshInstance[blockSize * blockSize * ((int)MeshLayer.Count - (int)MeshLayer.StaticCutout)];

                for (int i = 0; i < (int)MeshLayer.Count; i++)
                {
                    MeshLayer layer = (MeshLayer)i;
                    switch (layer)
                    {
                        case MeshLayer.StaticMaterial:
                        case MeshLayer.BaseMaterial:
                        case MeshLayer.LayerMaterial:
                        case MeshLayer.VeinMaterial:
                        case MeshLayer.NoMaterial:
                            FillMeshBuffer(out meshBuffer[bufferIndex], layer, tiles[xx, yy, block_z]);
                            bufferIndex++;
                            break;
                        case MeshLayer.StaticCutout:
                        case MeshLayer.BaseCutout:
                        case MeshLayer.LayerCutout:
                        case MeshLayer.VeinCutout:
                        case MeshLayer.Growth0Cutout:
                        case MeshLayer.Growth1Cutout:
                        case MeshLayer.Growth2Cutout:
                        case MeshLayer.Growth3Cutout:
                        case MeshLayer.NoMaterialCutout:
                            FillMeshBuffer(out stencilMeshBuffer[stencilBufferIndex], layer, tiles[xx, yy, block_z]);
                            stencilBufferIndex++;
                            break;
                        default:
                            break;
                    }
                }
            }
        if (blocks[block_x, block_y, block_z] == null)
        {
            GameObject block = Instantiate(defaultMapBlock) as GameObject;
            block.SetActive(true);
            block.transform.parent = this.transform;
            block.name = "terrain(" + block_x + ", " + block_y + ", " + block_z + ")";
            blocks[block_x, block_y, block_z] = block.GetComponent<MeshFilter>();
        }
        MeshFilter mf = blocks[block_x, block_y, block_z];
        if (mf == null)
            Debug.LogError("MF is null");
        if (mf.mesh == null)
            mf.mesh = new Mesh();
        mf.mesh.Clear();
        //mf.mesh.CombineMeshes(meshBuffer);
        if (stencilBlocks[block_x, block_y, block_z] == null)
        {
            GameObject stencilBlock = Instantiate(defaultStencilMapBLock) as GameObject;
            stencilBlock.SetActive(true);
            stencilBlock.transform.parent = this.transform;
            stencilBlock.name = "foliage(" + block_x + ", " + block_y + ", " + block_z + ")";
            stencilBlocks[block_x, block_y, block_z] = stencilBlock.GetComponent<MeshFilter>();
        }
        MeshFilter mfs = stencilBlocks[block_x, block_y, block_z];
        if (mfs == null)
            Debug.LogError("MFS is null");
        if (mfs.mesh == null)
            mfs.mesh = new Mesh();
        mfs.mesh.Clear();
        MeshCombineUtility.ColorCombine(mfs.mesh, stencilMeshBuffer);
        return MeshCombineUtility.ColorCombine(mf.mesh, meshBuffer);
        //Debug.Log("Generated a mesh with " + (mf.mesh.triangles.Length / 3) + " tris");
    }
    static int coord2Index(int x, int y)
    {
        return (x * (blockSize + 1)) + y;
    }
    void GenerateLiquidSurface(int block_x, int block_y, int block_z, int liquid_select)
    {
        Vector3[] finalVertices = new Vector3[(blockSize + 1) * (blockSize + 1)];
        Vector3[] finalNormals = new Vector3[(blockSize + 1) * (blockSize + 1)];
        Vector2[] finalUVs = new Vector2[(blockSize + 1) * (blockSize + 1)];
        List<int> finalFaces = new List<int>();
        float[,] heights = new float[2, 2];
        for (int xx = 0; xx <= blockSize; xx++)
            for (int yy = 0; yy <= blockSize; yy++)
            {
                //first find the heights of all tiles sharing one corner.
                for (int xxx = 0; xxx < 2; xxx++)
                    for (int yyy = 0; yyy < 2; yyy++)
                    {
                        int x = (block_x * blockSize) + xx + xxx - 1;
                        int y = (block_y * blockSize) + yy + yyy - 1;
                        if (x < 0 || y < 0 || x >= tiles.GetLength(0) || y >= tiles.GetLength(1))
                        {
                            heights[xxx, yyy] = -1;
                            continue;
                        }
                        var tile = tiles[x, y, block_z];
                        if (tile == null)
                        {
                            heights[xxx, yyy] = -1;
                            continue;
                        }
                        if (tile.isWall)
                        {
                            heights[xxx, yyy] = -1;
                            continue;
                        }
                        heights[xxx, yyy] = tile.liquid[liquid_select];
                        heights[xxx, yyy] /= 7.0f;
                        if (tile.isFloor)
                        {
                            heights[xxx, yyy] *= (tileHeight - floorHeight);
                            heights[xxx, yyy] += floorHeight;
                        }
                        else
                            heights[xxx, yyy] *= tileHeight;

                    }

                //now find their average, discaring invalid ones.
                float height = 0;
                float total = 0;
                foreach (var item in heights)
                {
                    if (item < 0)
                        continue;
                    height += item;
                    total++;
                }
                if (total >= 1)
                    height /= total;
                //find the slopes.
                float sx = ((
                    (heights[0, 0] < 0 ? height : heights[0, 0]) +
                    (heights[0, 1] < 0 ? height : heights[0, 1])) / 2) - ((
                    (heights[1, 0] < 0 ? height : heights[1, 0]) +
                    (heights[1, 1] < 0 ? height : heights[1, 1])) / 2);
                float sy = ((
                    (heights[0, 0] < 0 ? height : heights[0, 0]) +
                    (heights[1, 0] < 0 ? height : heights[1, 0])) / 2) - ((
                    (heights[0, 1] < 0 ? height : heights[0, 1]) +
                    (heights[1, 1] < 0 ? height : heights[1, 1])) / 2);
                finalNormals[coord2Index(xx, yy)] = new Vector3(sx, tileWidth * 2, -sy);
                finalNormals[coord2Index(xx, yy)].Normalize();

                finalVertices[coord2Index(xx, yy)] = DFtoUnityCoord(((block_x * blockSize) + xx), ((block_y * blockSize) + yy), block_z);
                finalVertices[coord2Index(xx, yy)].x -= tileWidth / 2.0f;
                finalVertices[coord2Index(xx, yy)].z += tileWidth / 2.0f;
                finalVertices[coord2Index(xx, yy)].y += height;
                finalUVs[coord2Index(xx, yy)] = new Vector2(xx, yy);
            }
        for (int xx = 0; xx < blockSize; xx++)
            for (int yy = 0; yy < blockSize; yy++)
            {
                if (tiles[(block_x * blockSize) + xx, (block_y * blockSize) + yy, block_z].liquid[liquid_select] == 0)
                    continue;
                finalFaces.Add(coord2Index(xx, yy));
                finalFaces.Add(coord2Index(xx + 1, yy));
                finalFaces.Add(coord2Index(xx + 1, yy + 1));

                finalFaces.Add(coord2Index(xx, yy));
                finalFaces.Add(coord2Index(xx + 1, yy + 1));
                finalFaces.Add(coord2Index(xx, yy + 1));
            }
        if (finalFaces.Count > 0)
        {
            if (liquidBlocks[block_x, block_y, block_z, liquid_select] == null)
            {
                GameObject block;
                if (liquid_select == l_magma)
                    block = Instantiate(defaultMagmaBlock) as GameObject;
                else
                    block = Instantiate(defaultWaterBlock) as GameObject;
                block.SetActive(true);
                block.transform.parent = this.transform;
                block.name = (liquid_select == l_water ? "water(" : "magma(") + block_x + ", " + block_y + ", " + block_z + ")";
                liquidBlocks[block_x, block_y, block_z, liquid_select] = block.GetComponent<MeshFilter>();
            }
        }
        MeshFilter mf = liquidBlocks[block_x, block_y, block_z, liquid_select];
        if (mf == null)
        {
            return;
        }
        if (mf.mesh == null)
            mf.mesh = new Mesh();
        mf.mesh.Clear();
        if (finalFaces.Count == 0)
            return;
        mf.mesh.vertices = finalVertices;
        mf.mesh.uv = finalUVs;
        mf.mesh.triangles = finalFaces.ToArray();
        mf.mesh.normals = finalNormals;
        //mf.mesh.RecalculateNormals();
        mf.mesh.RecalculateBounds();
        mf.mesh.RecalculateTangents();
    }


    void UpdateMeshes()
    {
        int count = 0;
        int failed = 0;
        for (int zz = connectionState.net_block_request.min_z; zz < connectionState.net_block_request.max_z; zz++)
            for (int yy = (connectionState.net_block_request.min_y * 16 / blockSize); yy <= (connectionState.net_block_request.max_y * 16 / blockSize); yy++)
                for (int xx = (connectionState.net_block_request.min_x * 16 / blockSize); xx <= (connectionState.net_block_request.max_x * 16 / blockSize); xx++)
                {
                    if (xx < 0 || yy < 0 || zz < 0 || xx >= blocks.GetLength(0) || yy >= blocks.GetLength(1) || zz >= blocks.GetLength(2))
                    {
                        //Debug.Log(xx + ", " + yy + ", " + zz + " is outside of " + blocks.GetLength(0) + ", " + blocks.GetLength(1) + ", " + blocks.GetLength(2));
                        continue;
                    }
                    //Debug.Log("Generating tiles at " + xx + ", " + yy + ", " + zz);
                    GenerateLiquids(xx, yy, zz);
                    if (GenerateTiles(xx, yy, zz))
                        count++;
                    else
                        failed++;
                }
        //Debug.Log("Generating " + count + " meshes took " + watch.ElapsedMilliseconds + " ms");
    }
    System.Diagnostics.Stopwatch netWatch = new System.Diagnostics.Stopwatch();
    System.Diagnostics.Stopwatch loadWatch = new System.Diagnostics.Stopwatch();
    System.Diagnostics.Stopwatch genWatch = new System.Diagnostics.Stopwatch();
    void GetBlockList()
    {
        netWatch.Reset();
        netWatch.Start();
        posX = (connectionState.net_view_info.view_pos_x + (connectionState.net_view_info.view_size_x / 2)) / 16;
        posY = (connectionState.net_view_info.view_pos_y + (connectionState.net_view_info.view_size_y / 2)) / 16;
        posZ = connectionState.net_view_info.view_pos_z + 1;
        connectionState.net_block_request.min_x = posX - rangeX;
        connectionState.net_block_request.max_x = posX + rangeX;
        connectionState.net_block_request.min_y = posY - rangeY;
        connectionState.net_block_request.max_y = posY + rangeY;
        connectionState.net_block_request.min_z = posZ - rangeZdown;
        connectionState.net_block_request.max_z = posZ + rangeZup;
        connectionState.net_block_request.blocks_needed = blocksToGet;
        connectionState.BlockListCall.execute(connectionState.net_block_request, out connectionState.net_block_list);
        netWatch.Stop();
    }
    void UseBlockList()
    {
        //stopwatch.Stop();
        //Debug.Log(connectionState.net_block_list.map_blocks.Count + " blocks gotten, took 1/" + (1.0 / stopwatch.Elapsed.TotalSeconds) + " seconds.\n");
        //for (int i = 0; i < blockCollection.Count; i++)
        //{
        //    if (blockCollection[i].gameObject.activeSelf == true)
        //    {
        //        blockCollection[i].Reposition(connectionState.net_block_list);
        //    }
        //}
        //watch.Start();
        //FreeAllBlocks();
        if ((connectionState.net_block_list.map_x != map_x) || (connectionState.net_block_list.map_y != map_y))
            ClearMap();
        map_x = connectionState.net_block_list.map_x;
        map_y = connectionState.net_block_list.map_y;
        loadWatch.Reset();
        loadWatch.Start();
        for (int i = 0; i < connectionState.net_block_list.map_blocks.Count; i++)
        {
            //MapBlock newBlock = getFreeBlock();
            //if (newBlock == null)
            //    break;
            //newBlock.gameObject.SetActive(true);
            //newBlock.SetAllTiles(connectionState.net_block_list.map_blocks[i], connectionState.net_block_list, connectionState.net_tiletype_list);
            //newBlock.Regenerate();
            //newBlock.name = "MapBlock(" + newBlock.coordString + ")";
            CopyTiles(connectionState.net_block_list.map_blocks[i]);
        }
        loadWatch.Stop();
        genWatch.Reset();
        genWatch.Start();
        UpdateMeshes();
        genWatch.Stop();
        genStatus.text = connectionState.net_block_list.map_blocks.Count + " blocks gotten.\n"
            + netWatch.ElapsedMilliseconds + "ms network time\n"
            + loadWatch.ElapsedMilliseconds + "ms map copy \n" + genWatch.ElapsedMilliseconds + "ms mesh generation";
        //watch.Stop();
        //Debug.Log("Generating " + connectionState.net_block_list.map_blocks.Count + " Meshes took " + watch.Elapsed.TotalSeconds + " seconds");
    }

    int lastLoadedLevel = 0;
    void LazyLoadBlocks()
    {
        lastLoadedLevel--;
        if (lastLoadedLevel < 0 || lastLoadedLevel < connectionState.net_view_info.view_pos_z + 1 - viewCamera.viewDist)
            lastLoadedLevel = connectionState.net_view_info.view_pos_z + 1 - rangeZdown;
        posX = (connectionState.net_view_info.view_pos_x + (connectionState.net_view_info.view_size_x / 2)) / 16;
        posY = (connectionState.net_view_info.view_pos_y + (connectionState.net_view_info.view_size_y / 2)) / 16;
        connectionState.net_block_request.min_x = posX - rangeX;
        connectionState.net_block_request.max_x = posX + rangeX;
        connectionState.net_block_request.min_y = posY - rangeY;
        connectionState.net_block_request.max_y = posY + rangeY;
        connectionState.net_block_request.min_z = lastLoadedLevel;
        connectionState.net_block_request.max_z = lastLoadedLevel + 1;

        connectionState.BlockListCall.execute(connectionState.net_block_request, out connectionState.net_block_list);

        if ((connectionState.net_block_list.map_x != map_x) || (connectionState.net_block_list.map_y != map_y))
            ClearMap();
        map_x = connectionState.net_block_list.map_x;
        map_y = connectionState.net_block_list.map_y;

        for (int i = 0; i < connectionState.net_block_list.map_blocks.Count; i++)
        {
            CopyTiles(connectionState.net_block_list.map_blocks[i]);
        }

        UpdateMeshes();
    }

    void CullDistantBlocks()
    {
        int dist = viewCamera.viewDist;
        int centerZ = connectionState.net_view_info.view_pos_z;
        //int centerX = (connectionState.net_view_info.view_pos_x + (connectionState.net_view_info.view_size_x / 2));
        //int centerY = (connectionState.net_view_info.view_pos_y + (connectionState.net_view_info.view_size_y / 2));
        for (int xx = 0; xx < blocks.GetLength(0); xx++)
            for (int yy = 0; yy < blocks.GetLength(1); yy++)
                for (int zz = 0; zz < blocks.GetLength(2); zz++)
                {
                    if (zz > centerZ + dist)
                    {
                        if (blocks[xx, yy, zz] != null)
                        {
                            blocks[xx, yy, zz].mesh.Clear();
                            blockDirtyBits[xx, yy, zz] = true;

                        }
                        if (stencilBlocks[xx, yy, zz] != null)
                        {
                            stencilBlocks[xx, yy, zz].mesh.Clear();
                            blockDirtyBits[xx, yy, zz] = true;

                        }
                        for (int i = 0; i < 2; i++)
                            if (liquidBlocks[xx, yy, zz, i] != null)
                            {
                                liquidBlocks[xx, yy, zz, i].mesh.Clear();
                                waterBlockDirtyBits[xx, yy, zz] = true;
                            }
                        continue;
                    }
                    if (zz < centerZ - dist)
                    {
                        if (blocks[xx, yy, zz] != null)
                        {
                            blocks[xx, yy, zz].mesh.Clear();
                            blockDirtyBits[xx, yy, zz] = true;

                        }
                        if (stencilBlocks[xx, yy, zz] != null)
                        {
                            stencilBlocks[xx, yy, zz].mesh.Clear();
                            blockDirtyBits[xx, yy, zz] = true;

                        } 
                        for (int i = 0; i < 2; i++)
                            if (liquidBlocks[xx, yy, zz, i] != null)
                            {
                                liquidBlocks[xx, yy, zz, i].mesh.Clear();
                                waterBlockDirtyBits[xx, yy, zz] = true;
                            }
                        continue;
                    }
                    //int distSide = dist;// / 16;
                    //if (xx > centerX + distSide)
                    //{
                    //    blocks[xx, yy, zz].mesh.Clear();
                    //    continue;
                    //}
                    //if (xx < centerX - distSide)
                    //{
                    //    blocks[xx, yy, zz].mesh.Clear();
                    //    continue;
                    //}
                    //if (yy > centerY + distSide)
                    //{
                    //    blocks[xx, yy, zz].mesh.Clear();
                    //    continue;
                    //}
                    //if (yy < centerY - distSide)
                    //{
                    //    blocks[xx, yy, zz].mesh.Clear();
                    //    continue;
                    //}

                }
    }

    void GetUnitList()
    {
//        System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
//        stopwatch.Start();
        connectionState.UnitListCall.execute(null, out connectionState.net_unit_list);
//        stopwatch.Stop();
//        Debug.Log(connectionState.net_unit_list.creature_list.Count + " units gotten, took " + stopwatch.Elapsed.Milliseconds + " ms.");

    }
    void GetViewInfo()
    {
        connectionState.ViewInfoCall.execute(null, out connectionState.net_view_info);
    }

    void PositionCamera()
    {
        viewCamera.transform.parent.transform.position = MapBlock.DFtoUnityCoord(
            (connectionState.net_view_info.view_pos_x + (connectionState.net_view_info.view_size_x / 2)),
            (connectionState.net_view_info.view_pos_y + (connectionState.net_view_info.view_size_y / 2)),
            connectionState.net_view_info.view_pos_z + 1);
        viewCamera.viewWidth = connectionState.net_view_info.view_size_x;
        viewCamera.viewHeight = connectionState.net_view_info.view_size_y;
    }

    void GetMapInfo()
    {
        connectionState.MapInfoCall.execute(null, out connectionState.net_map_info);
    }

    void ClearMap()
    {
        foreach (MeshFilter MF in blocks)
        {
            if (MF != null)
                MF.mesh.Clear();
        }
        foreach (var item in stencilBlocks)
        {
            if (item != null)
                item.mesh.Clear();
        }
        foreach (var item in liquidBlocks)
        {
            if (item != null)
                item.mesh.Clear();   
        }
        foreach (var tile in tiles)
        {
            if (tile != null)
            {
                tile.tileType = 0;
                tile.material.mat_index = -1;
                tile.material.mat_type = -1;
            }
        }
        foreach (var item in magmaGlow)
        {
            Destroy(item);
        }
    }

    void HideMeshes()
    {
        for (int zz = 0; zz < blocks.GetLength(2); zz++)
            for (int yy = 0; yy < blocks.GetLength(1); yy++)
                for (int xx = 0; xx < blocks.GetLength(0); xx++)
                {
                    if (blocks[xx, yy, zz] != null)
                    {
                        if (zz > connectionState.net_view_info.view_pos_z)
                        {
                            blocks[xx, yy, zz].gameObject.GetComponent<Renderer>().material = invisibleMaterial;
                            //blocks[xx, yy, zz].gameObject.SetActive(false);
                        }
                        else
                        {
                            blocks[xx, yy, zz].gameObject.GetComponent<Renderer>().material = basicTerrainMaterial;
                            //blocks[xx, yy, zz].gameObject.SetActive(true);
                        }
                    }
                }
        for (int zz = 0; zz < stencilBlocks.GetLength(2); zz++)
            for (int yy = 0; yy < stencilBlocks.GetLength(1); yy++)
                for (int xx = 0; xx < stencilBlocks.GetLength(0); xx++)
                {
                    if (stencilBlocks[xx, yy, zz] != null)
                    {
                        if (zz > connectionState.net_view_info.view_pos_z)
                        {
                            stencilBlocks[xx, yy, zz].gameObject.GetComponent<Renderer>().material = invisibleStencilMaterial;
                            //stencilBlocks[xx, yy, zz].gameObject.SetActive(false);
                        }
                        else
                        {
                            stencilBlocks[xx, yy, zz].gameObject.GetComponent<Renderer>().material = stencilTerrainMaterial;
                            //stencilBlocks[xx, yy, zz].gameObject.SetActive(true);
                        }
                    }
                }
        for (int qq = 0; qq < liquidBlocks.GetLength(3); qq++)
            for (int zz = 0; zz < liquidBlocks.GetLength(2); zz++)
                for (int yy = 0; yy < liquidBlocks.GetLength(1); yy++)
                    for (int xx = 0; xx < liquidBlocks.GetLength(0); xx++)
                    {
                        if (liquidBlocks[xx, yy, zz, qq] != null)
                        {
                            if (zz > connectionState.net_view_info.view_pos_z)
                                liquidBlocks[xx, yy, zz, qq].gameObject.GetComponent<Renderer>().material = invisibleMaterial;
                            else
                            {
                                if(qq == l_magma)
                                    liquidBlocks[xx, yy, zz, qq].gameObject.GetComponent<Renderer>().material = magmaMaterial;
                                else
                                    liquidBlocks[xx, yy, zz, qq].gameObject.GetComponent<Renderer>().material = waterMaterial;
                            }
                        }
                    }
    }

    void ShowCursorInfo()
    {
        int cursX = connectionState.net_view_info.cursor_pos_x;
        int cursY = connectionState.net_view_info.cursor_pos_y;
        int cursZ = connectionState.net_view_info.cursor_pos_z;
        cursorProperties.text = "";
        cursorProperties.text += "Cursor: ";
        cursorProperties.text += cursX + ",";
        cursorProperties.text += cursY + ",";
        cursorProperties.text += cursZ + "\n";
        if (
            cursX >= 0 &&
            cursY >= 0 &&
            cursZ >= 0 &&
            cursX < tiles.GetLength(0) &&
            cursY < tiles.GetLength(1) &&
            cursZ < tiles.GetLength(2) &&
            tiles[cursX, cursY, cursZ] != null)
        {
            cursorProperties.text += "Tiletype:\n";
            var tiletype = connectionState.net_tiletype_list.tiletype_list[tiles[cursX, cursY, cursZ].tileType];
            cursorProperties.text += tiletype.name + "\n";
            cursorProperties.text +=
                tiletype.shape + ":" +
                tiletype.special + ":" +
                tiletype.material + ":" +
                tiletype.variant + ":" +
                tiletype.direction + "\n";
            var mat = tiles[cursX, cursY, cursZ].material;
            cursorProperties.text += "Material: ";
            cursorProperties.text += mat.mat_type + ",";
            cursorProperties.text += mat.mat_index + "\n";

            if (materials.ContainsKey(mat))
            {
                cursorProperties.text += "Material Name: ";
                cursorProperties.text += materials[mat].id + "\n";
            }
            else
                cursorProperties.text += "Unknown Material\n";

            cursorProperties.text += "\n";

            var basemat = tiles[cursX, cursY, cursZ].base_material;
            cursorProperties.text += "Base Material: ";
            cursorProperties.text += basemat.mat_type + ",";
            cursorProperties.text += basemat.mat_index + "\n";

            if (materials.ContainsKey(basemat))
            {
                cursorProperties.text += "Base Material Name: ";
                cursorProperties.text += materials[basemat].id + "\n";
            }
            else
                cursorProperties.text += "Unknown Base Material\n";

            cursorProperties.text += "\n";

            var layermat = tiles[cursX, cursY, cursZ].layer_material;
            cursorProperties.text += "Layer Material: ";
            cursorProperties.text += layermat.mat_type + ",";
            cursorProperties.text += layermat.mat_index + "\n";

            if (materials.ContainsKey(layermat))
            {
                cursorProperties.text += "Layer Material Name: ";
                cursorProperties.text += materials[layermat].id + "\n";
            }
            else
                cursorProperties.text += "Unknown Layer Material\n";

            cursorProperties.text += "\n";

            var veinmat = tiles[cursX, cursY, cursZ].vein_material;
            cursorProperties.text += "Vein Material: ";
            cursorProperties.text += veinmat.mat_type + ",";
            cursorProperties.text += veinmat.mat_index + "\n";

            if (materials.ContainsKey(veinmat))
            {
                cursorProperties.text += "Vein Material Name: ";
                cursorProperties.text += materials[veinmat].id + "\n";
            }
            else
                cursorProperties.text += "Unknown Vein Material\n";

        }
    }

    Dictionary<int, GameObject> creatureList;
    public GameObject creatureTemplate;

    void UpdateCreatures()
    {
        foreach (var unit in connectionState.net_unit_list.creature_list)
        {
            if (creatureList == null)
                creatureList = new Dictionary<int, GameObject>();
            if (!creatureList.ContainsKey(unit.id))
            {
                creatureList[unit.id] = Instantiate(creatureTemplate) as GameObject;
                creatureList[unit.id].GetComponent<LayeredSprite>().Do_Sprite = true;
                creatureList[unit.id].transform.parent = gameObject.transform;
            }
            creatureList[unit.id].transform.position = DFtoUnityCoord(unit.pos_x, unit.pos_y, unit.pos_z) + new Vector3(0, 2, 0);
        }
    }

}
