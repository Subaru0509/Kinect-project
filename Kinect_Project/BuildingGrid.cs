using System;
using System.Drawing;

namespace Kinect_Project
{
    public enum CellType
    {
        Wall,   // 墙壁，不透光且无法交互
        Dirty,  // 顽固污渍
        Foamed, // 喷上清洁剂的软化泡沫
        Clean   // 干净的玻璃
    }

    public class BuildingGrid : IDisposable
    {
        public const int Cols = 20;
        public const int Rows = 15;
        // 核心改动：划分边缘为墙壁区域，避开Kinect边缘识别不稳定的盲区
        private const int WallThickness = 3; // 左右各3格宽度的边缘墙壁

        private CellType[,] grid = new CellType[Cols, Rows];

        private SolidBrush dirtyBrush = new SolidBrush(Color.FromArgb(200, 130, 110, 90));
        private SolidBrush foamedBrush = new SolidBrush(Color.FromArgb(120, 200, 240, 255));
        private SolidBrush wallBrush = new SolidBrush(Color.FromArgb(255, 70, 70, 75)); // 水泥灰墙面色
        private Pen windowFramePen = new Pen(Color.FromArgb(80, 40, 40, 40), 10f);

        public int CleanWindowCount { get; private set; }
        public int TotalWindowCount { get; private set; }

        public BuildingGrid()
        {
            TotalWindowCount = (Cols - WallThickness * 2) * Rows;
            InitializeGrid();
        }

        public void InitializeGrid()
        {
            for (int y = 0; y < Rows; y++)
                GenerateRow(y);
            CleanWindowCount = 0;
        }

        public void ClearGrid()
        {
            for (int x = 0; x < Cols; x++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    grid[x, y] = (x < WallThickness || x >= Cols - WallThickness) ? CellType.Wall : CellType.Clean;
                }
            }
        }

        public void GenerateRow(int y)
        {
            for (int x = 0; x < Cols; x++)
            {
                // 如果是左右两边的边缘格子，直接判定为墙壁；中间的部分则生成为脏玻璃
                if (x < WallThickness || x >= Cols - WallThickness)
                {
                    grid[x, y] = CellType.Wall;
                }
                else
                {
                    grid[x, y] = CellType.Dirty;
                }
            }
        }

        public void DropAnimationStep()
        {
            // 所有行上移 (模拟吊篮下降)
            for (int y = 0; y < Rows - 1; y++)
            {
                for (int x = 0; x < Cols; x++)
                {
                    grid[x, y] = grid[x, y + 1];
                }
            }
            // 底部生成新的一层污垢/墙壁
            GenerateRow(Rows - 1);
        }

        public void ResetCleanCount()
        {
            CleanWindowCount = 0;
        }

        public bool IsAreaCleaned(float threshold = 0.95f)
        {
            return CleanWindowCount >= TotalWindowCount * threshold;
        }

        public void ProcessWipe(float handXRatio, float handYRatio, bool isLeftHand)
        {
            int gridX = (int)(handXRatio * Cols);
            int gridY = (int)(handYRatio * Rows);
            int brushRadius = 1;

            for (int x = gridX - brushRadius; x <= gridX + brushRadius; x++)
            {
                for (int y = gridY - brushRadius; y <= gridY + brushRadius; y++)
                {
                    if (x >= 0 && x < Cols && y >= 0 && y < Rows)
                    {
                        // 墙壁区域无法擦拭
                        if (grid[x, y] == CellType.Wall) continue;

                        if (isLeftHand)
                        {
                            // 左手喷上清洁泡沫
                            if (grid[x, y] == CellType.Dirty)
                                grid[x, y] = CellType.Foamed;
                        }
                        else
                        {
                            // 右手擦除泡沫变干净
                            if (grid[x, y] == CellType.Foamed)
                            {
                                grid[x, y] = CellType.Clean;
                                CleanWindowCount++;
                            }
                        }
                    }
                }
            }
        }

        public void Draw(Graphics g, float viewWidth, float viewHeight)
        {
            float cellWidth = viewWidth / Cols;
            float cellHeight = viewHeight / Rows;

            // 1. 绘制网格状态
            for (int x = 0; x < Cols; x++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    // +1 是避免图形浮点取整时有微小接缝
                    if (grid[x, y] == CellType.Wall)
                        g.FillRectangle(wallBrush, x * cellWidth, y * cellHeight, cellWidth + 1, cellHeight + 1);
                    else if (grid[x, y] == CellType.Dirty)
                        g.FillRectangle(dirtyBrush, x * cellWidth, y * cellHeight, cellWidth + 1, cellHeight + 1);
                    else if (grid[x, y] == CellType.Foamed)
                        g.FillRectangle(foamedBrush, x * cellWidth, y * cellHeight, cellWidth + 1, cellHeight + 1);
                }
            }

            // 2. 绘制窗框 (只在窗户区域内绘制)
            int paneWidthCells = 7; // 横向分成两扇大大的玻璃 (总有效宽14，分成7和7)
            int paneHeightCells = 5; // 纵向分成三格大玻璃

            for (int x = WallThickness; x <= Cols - WallThickness; x += paneWidthCells)
            {
                g.DrawLine(windowFramePen, x * cellWidth, 0, x * cellWidth, viewHeight);
            }
            for (int y = 0; y <= Rows; y += paneHeightCells)
            {
                // 横窗框不需要跨越左右两边的墙体
                g.DrawLine(windowFramePen, WallThickness * cellWidth, y * cellHeight, (Cols - WallThickness) * cellWidth, y * cellHeight);
            }
        }

        public void Dispose()
        {
            dirtyBrush?.Dispose();
            foamedBrush?.Dispose();
            wallBrush?.Dispose();
            windowFramePen?.Dispose();
        }
    }
}