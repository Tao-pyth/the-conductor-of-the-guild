using Godot;
using Guild.Application;

namespace Guild.Game;

/// <summary>Read-only tactical board. All drawing and input use the same square-cell geometry.</summary>
public partial class GuildMap : Control
{
    public Func<GameView?> ReadView { get; set; } = () => null;
    public Func<string?> SelectedActor { get; set; } = () => null;
    public Action<int, int>? CellSelected { get; set; }
    public Font? Font { get; set; }

    private static readonly Color BoardPaper = new("faf8ec");
    private static readonly Color BoardInk = new("294d46");
    private static readonly Color GridInk = new("8faa70");
    private const float CoordinateLeft = 36;
    private const float CoordinateTop = 32;
    private const float BoardEdge = 10;

    private readonly record struct BoardGeometry(Vector2 Origin, float Cell, Rect2 Bounds);

    private BoardGeometry GetBoardGeometry(GameView view)
    {
        if (view.MapWidth <= 0 || view.MapHeight <= 0) return new(Vector2.Zero, 0, new Rect2());
        var available = new Vector2(Math.Max(0, Size.X - CoordinateLeft - BoardEdge),
            Math.Max(0, Size.Y - CoordinateTop - BoardEdge));
        var cell = Math.Max(0, Math.Min(available.X / view.MapWidth, available.Y / view.MapHeight));
        var extent = new Vector2(cell * view.MapWidth, cell * view.MapHeight);
        var origin = new Vector2(CoordinateLeft, CoordinateTop) + (available - extent) / 2;
        return new(origin, cell, new Rect2(origin, extent));
    }

    private static Vector2 CellCenter(BoardGeometry geometry, double x, double y)
        => geometry.Origin + new Vector2((float)x + .5f, (float)y + .5f) * geometry.Cell;

    public Rect2 GetBoardRectForSmoke() => ReadView() is { } view ? GetBoardGeometry(view).Bounds : new Rect2();
    public float BoardCellSizeForSmoke => ReadView() is { } view ? GetBoardGeometry(view).Cell : 0;
    public Vector2 CellCenterForSmoke(int x, int y)
        => ReadView() is { } view ? CellCenter(GetBoardGeometry(view), x, y) : Vector2.Zero;

    public override void _Draw()
    {
        var view = ReadView();
        if (view is null) return;
        var geometry = GetBoardGeometry(view);
        if (geometry.Cell <= 0) return;
        var cell = geometry.Cell;
        var origin = geometry.Origin;
        DrawRect(new Rect2(Vector2.Zero, Size), BoardPaper);
        DrawRect(geometry.Bounds.Grow(3), new Color("d1bb86"), false, 2);
        for (var y = 0; y < view.MapHeight; y++)
            for (var x = 0; x < view.MapWidth; x++)
            {
                var rect = new Rect2(origin + new Vector2(x * cell, y * cell), new Vector2(cell, cell));
                var path = view.Path.Any(point => point.X == x && point.Y == y);
                var color = path ? new Color("edd4a6")
                    : IsDeploymentCell(view, x, y) ? new Color((x + y) % 2 == 0 ? "cde2a7" : "c9dfa2")
                    : new Color("e3e8d1");
                DrawRect(rect, color);
                DrawRect(rect, path ? new Color("c3a977") : GridInk, false, 1);
            }
        DrawBoardCoordinates(view, geometry);
        DrawPreparationRanges(view, origin, cell);
        if (view.Mode != "invasion" && view.Path.Count > 0)
        {
            var goal = view.Path[^1];
            DrawObjective(CellCenter(geometry, goal.X, goal.Y), cell, DisplayText.Get("ui.mode.defense"), new Color("507e72"), geometry.Bounds);
        }
        foreach (var actor in view.Adventurers.Where(actor => actor.Deployed))
            DrawAdventurer(view, geometry, actor);
        foreach (var entity in view.Entities)
        {
            if (view.Adventurers.Any(actor => actor.Id == entity.Id)) continue;
            DrawMapEntity(geometry, entity);
        }
        if (Font is not null && view.Path.Count > 0)
        {
            var entrance = view.Path[0];
            var rect = new Rect2(origin + new Vector2(entrance.X * cell + 2, entrance.Y * cell + 3),
                new Vector2(Math.Max(0, cell - 4), Math.Min(cell - 6, 29)));
            DrawRect(rect, new Color("f9edcf"));
            DrawRect(rect, new Color("a7844d"), false, 1);
            DrawString(Font, rect.Position + new Vector2(0, rect.Size.Y * .75f), DisplayText.Get("ui.map.001"),
                HorizontalAlignment.Center, rect.Size.X, (int)Math.Clamp(cell * .28f, 16, 20), BoardInk);
        }
    }

    private void DrawBoardCoordinates(GameView view, BoardGeometry geometry)
    {
        if (Font is null) return;
        var fontSize = (int)Math.Clamp(geometry.Cell * .35f, 20, 24);
        for (var x = 0; x < view.MapWidth; x++)
            DrawString(Font, geometry.Origin + new Vector2(x * geometry.Cell, -9), (x + 1).ToString(),
                HorizontalAlignment.Center, geometry.Cell, fontSize, BoardInk);
        for (var y = 0; y < view.MapHeight; y++)
            DrawString(Font, geometry.Origin + new Vector2(-CoordinateLeft + 2, (y + .5f) * geometry.Cell + fontSize * .34f),
                (y + 1).ToString(), HorizontalAlignment.Center, CoordinateLeft - 8, fontSize, BoardInk);
    }

    private void DrawAdventurer(GameView view, BoardGeometry geometry, AdventurerView actor)
    {
        var cell = geometry.Cell;
        var entity = view.Entities.FirstOrDefault(item => item.Id == actor.Id);
        var point = entity is not null ? CellCenter(geometry, entity.X, entity.Y) : CellCenter(geometry, actor.X, actor.Y);
        var selected = actor.Id == SelectedActor();
        var radius = cell * .30f;
        DrawCircle(point + new Vector2(1, 2), radius + 2, new Color(.15f, .25f, .2f, .15f));
        if (selected)
        {
            DrawCircle(point, radius + 6, new Color("fffdf1"));
            DrawArc(point, radius + 6, 0, Mathf.Tau, 48, new Color("195da3"), 3, true);
        }
        DrawCircle(point, radius, actor.Incapacitated ? new Color("dadbd0") : new Color("b5dafa"));
        DrawArc(point, radius, 0, Mathf.Tau, 40, actor.Incapacitated ? new Color("737e73") : new Color("2467af"), 3, true);
        var weapon = CivicAssets.Weapon(actor.WeaponType);
        if (weapon is not null)
            DrawTextureRect(weapon, new Rect2(point - Vector2.One * radius * .88f, Vector2.One * radius * 1.76f), false,
                actor.Incapacitated ? new Color("8a9188") : Colors.White);
        else
            DrawCircle(point, radius * .38f, actor.Incapacitated ? new Color("8a9188") : new Color("2467af"));
        var hp = entity?.HpRatio ?? ParseHealthRatio(actor);
        DrawHealthBar(point + new Vector2(-cell * .30f, cell * .39f), cell * .60f, Math.Max(4, cell * .07f), hp,
            actor.Incapacitated ? new Color("7a8076") : new Color("4c9b69"));
        if (Font is not null && (selected || view.Screen == "Preparation"))
        {
            var fontSize = (int)Math.Clamp(cell * .31f, 18, 23);
            var available = Math.Min(cell * 2.25f, 160);
            var label = FitMapLabel(actor.Name, fontSize, available);
            var width = Font.GetStringSize(label, HorizontalAlignment.Left, -1, fontSize).X + 12;
            var labelRect = new Rect2(point + new Vector2(-width / 2, -cell * .45f - fontSize), new Vector2(width, fontSize + 6));
            labelRect.Position = new Vector2(Math.Clamp(labelRect.Position.X, geometry.Bounds.Position.X,
                Math.Max(geometry.Bounds.Position.X, geometry.Bounds.End.X - labelRect.Size.X)),
                Math.Max(geometry.Bounds.Position.Y + 2, labelRect.Position.Y));
            DrawRect(labelRect, new Color("fffcef"));
            DrawString(Font, labelRect.Position + new Vector2(6, fontSize), label, HorizontalAlignment.Left, -1, fontSize, BoardInk);
        }
    }

    private void DrawMapEntity(BoardGeometry geometry, MapEntityView entity)
    {
        var cell = geometry.Cell;
        var point = CellCenter(geometry, entity.X, entity.Y);
        var projectile = entity.Kind.Contains("projectile", StringComparison.OrdinalIgnoreCase);
        if (entity.Kind is "gate" or "core")
            DrawObjective(point, cell, entity.Kind == "core" ? DisplayText.Get("ui.map.002") : DisplayText.Get("ui.map.003"), new Color(entity.Kind == "core" ? "87576d" : "9a7958"), geometry.Bounds, entity.Kind == "core");
        else if (entity.Kind == "turret")
        {
            var rect = new Rect2(point - Vector2.One * cell * .25f, Vector2.One * cell * .50f);
            DrawRect(rect, new Color("d4a257"));
            DrawRect(rect, new Color("77552c"), false, 2);
            DrawLine(point, point + new Vector2(-cell * .33f, 0), new Color("77552c"), Math.Max(3, cell * .08f), true);
        }
        else
        {
            DrawCircle(point, cell * (projectile ? .07f : .24f), new Color(projectile ? "ad6c32" : "c96c62"));
            if (!projectile) DrawArc(point, cell * .24f, 0, Mathf.Tau, 32, new Color("8c3f37"), 2, true);
        }
        if (!projectile)
            DrawHealthBar(point + new Vector2(-cell * .28f, -cell * .42f), cell * .56f,
                Math.Max(4, cell * .07f), entity.HpRatio, new Color("ba5b4e"));
    }

    private void DrawObjective(Vector2 point, float cell, string text, Color color, Rect2 board, bool isCore = false)
    {
        var gate = CivicAssets.Texture("gate-token");
        if (gate is not null && gate.GetWidth() > 0 && gate.GetHeight() > 0)
        {
            var original = gate.GetSize();
            var scale = Math.Min(cell * .95f / original.X, cell * 1.10f / original.Y);
            var size = original * scale;
            var position = point - new Vector2(size.X / 2, size.Y * .63f);
            position = new Vector2(Math.Clamp(position.X, board.Position.X, Math.Max(board.Position.X, board.End.X - size.X)),
                Math.Clamp(position.Y, board.Position.Y, Math.Max(board.Position.Y, board.End.Y - size.Y)));
            if (isCore)
                DrawArc(point, cell * .48f, 0, Mathf.Tau, 48, color, 3, true);
            DrawTextureRect(gate, new Rect2(position, size), false);
            if (Font is not null)
            {
                var fontSize = (int)Math.Clamp(cell * .28f, 16, 21);
                var labelSize = new Vector2(Font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X + 10, fontSize + 5);
                var labelPosition = point + new Vector2(-labelSize.X / 2, cell * .43f);
                labelPosition = new Vector2(Math.Clamp(labelPosition.X, board.Position.X, Math.Max(board.Position.X, board.End.X - labelSize.X)),
                    Math.Clamp(labelPosition.Y, board.Position.Y, Math.Max(board.Position.Y, board.End.Y - labelSize.Y)));
                var label = new Rect2(labelPosition, labelSize);
                DrawRect(label, new Color("fff7df"));
                DrawRect(label, color, false, 1);
                DrawString(Font, label.Position + new Vector2(5, fontSize), text,
                    HorizontalAlignment.Left, -1, fontSize, color);
            }
            return;
        }
        var rect = new Rect2(point - Vector2.One * cell * .29f, Vector2.One * cell * .58f);
        DrawRect(rect, new Color("fff4d5"));
        DrawRect(rect, color, false, 3);
        if (Font is not null)
            DrawString(Font, rect.Position + new Vector2(-cell * .2f, rect.Size.Y * .71f), text,
                HorizontalAlignment.Center, rect.Size.X + cell * .4f, (int)Math.Clamp(cell * .29f, 16, 21), color);
    }

    private void DrawHealthBar(Vector2 position, float width, float height, double ratio, Color color)
    {
        DrawRect(new Rect2(position, new Vector2(width, height)), new Color("d0d8c8"));
        DrawRect(new Rect2(position, new Vector2(width * (float)Math.Clamp(ratio, 0, 1), height)), color);
    }

    private static double ParseHealthRatio(AdventurerView actor)
        => double.TryParse(actor.Hp, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hp)
            && double.TryParse(actor.MaxHp, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var max)
            && max > 0 ? Math.Clamp(hp / max, 0, 1) : actor.Incapacitated ? 0 : 1;

    private string FitMapLabel(string text, int fontSize, float width)
    {
        if (Font is null || Font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X <= width) return text;
        var result = text;
        while (result.Length > 1 && Font.GetStringSize(result + "…", HorizontalAlignment.Left, -1, fontSize).X > width)
            result = result[..^1];
        return result + "…";
    }

    public override void _GuiInput(InputEvent @event)
    {
        TrackRangePointer(@event);
        UpdateMapTooltip(@event);
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
        var view = ReadView();
        if (view is null) return;
        var geometry = GetBoardGeometry(view);
        if (geometry.Cell <= 0) return;
        var x = (int)Math.Floor((click.Position.X - geometry.Origin.X) / geometry.Cell);
        var y = (int)Math.Floor((click.Position.Y - geometry.Origin.Y) / geometry.Cell);
        if (x >= 0 && y >= 0 && x < view.MapWidth && y < view.MapHeight && IsDeploymentCell(view, x, y)) CellSelected?.Invoke(x, y);
        AcceptEvent();
    }

    private void UpdateMapTooltip(InputEvent input)
    {
        if (input is not InputEventMouseMotion motion || ReadView() is not { } view) return;
        var geometry = GetBoardGeometry(view);
        var actor = view.Adventurers.Where(item => item.Deployed).FirstOrDefault(item =>
        {
            var entity = view.Entities.FirstOrDefault(candidate => candidate.Id == item.Id);
            var point = entity is null ? CellCenter(geometry, item.X, item.Y) : CellCenter(geometry, entity.X, entity.Y);
            return motion.Position.DistanceTo(point) <= geometry.Cell * .40f;
        });
        TooltipText = actor is null ? "" : DisplayText.Format("ui.map.tooltip", actor.Name, actor.Weapon, actor.Hp, actor.MaxHp, actor.Activity);
    }

    public void ClickCellForSmoke(int x, int y)
    {
        var view = ReadView() ?? throw new InvalidOperationException(DisplayText.Get("ui.map.004"));
        _GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left,
            Position = CellCenter(GetBoardGeometry(view), x, y) });
    }

    private static bool IsDeploymentCell(GameView view, int x, int y)
        => view.DeploymentCells.Any(point => point.X == x && point.Y == y);
}
