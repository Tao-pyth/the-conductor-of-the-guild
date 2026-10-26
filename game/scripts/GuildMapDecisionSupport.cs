using Godot;
using Guild.Application;

namespace Guild.Game;

public partial class GuildMap
{
    public Func<bool> ShowPreparationRanges { get; set; } = () => false;
    public Func<bool> RepositionPreview { get; set; } = () => false;
    public Func<string> SelectedRangeSkill { get; set; } = () => "";
    private (int X, int Y)? _rangeCandidate;
    private Rect2 _rangeClip;

    public override void _Ready()
    {
        ClipContents = true;
        Resized += QueueRedraw;
        MouseExited += () => { _rangeCandidate = null; TooltipText = ""; QueueRedraw(); };
    }

    private void TrackRangePointer(InputEvent input)
    {
        if (input is not InputEventMouseMotion motion) return;
        var view = ReadView();
        if (view?.Screen != "Preparation") { _rangeCandidate = null; return; }
        var geometry = GetBoardGeometry(view);
        if (geometry.Cell <= 0) return;
        var x = (int)Math.Floor((motion.Position.X - geometry.Origin.X) / geometry.Cell);
        var y = (int)Math.Floor((motion.Position.Y - geometry.Origin.Y) / geometry.Cell);
        _rangeCandidate = (x, y);
        QueueRedraw();
    }

    private void DrawPreparationRanges(GameView view, Vector2 origin, float cell)
    {
        if (view.Screen != "Preparation" || !ShowPreparationRanges()) return;
        var actor = view.Adventurers.FirstOrDefault(item => item.Id == SelectedActor());
        if (actor?.RangeGuide is not { Available: true } guide || actor.Incapacitated) return;
        var ghost = _rangeCandidate is { } candidate && (!actor.Deployed || RepositionPreview())
            && candidate.X >= 0 && candidate.X < view.MapWidth && candidate.Y >= 0 && candidate.Y < view.MapHeight
            && IsDeploymentCell(view, candidate.X, candidate.Y)
            && !view.Adventurers.Any(other => other.Id != actor.Id && other.Deployed && other.X == candidate.X && other.Y == candidate.Y);
        if (!actor.Deployed && !ghost) return;
        var actual = ghost
            ? new Vector2(_rangeCandidate!.Value.X + .5f, _rangeCandidate.Value.Y + .5f)
            : new Vector2((float)guide.ActualX + .5f, (float)guide.ActualY + .5f);
        var anchor = ghost ? actual : new Vector2((float)guide.AnchorX + .5f, (float)guide.AnchorY + .5f);
        var center = origin + actual * cell;
        var anchorCenter = origin + anchor * cell;
        _rangeClip = new Rect2(origin, new Vector2(view.MapWidth * cell, view.MapHeight * cell));
        DrawGuideCircle(anchorCenter, (float)guide.ActionRadius * cell, new Color("a36d24"), 2);
        DrawGuideCircle(center, (float)guide.WatchRadius * cell, new Color("456ca2"), 1);
        DrawGuideCircle(center, (float)guide.AttackRadius * cell, new Color("226778"), 0);
        var skill = guide.SkillRadii.FirstOrDefault(item => item.Slot == SelectedRangeSkill());
        if (skill is not null)
            DrawGuideCircle(center, (float)skill.Radius * cell, skill.IsHealing ? new Color("008a8a") : new Color("a05b89"), 3);
        if (ghost)
        {
            DrawRect(new Rect2(center - Vector2.One * cell * .34f, Vector2.One * cell * .68f), new Color("a14f35"), false, 3);
            if (Font is not null)
                DrawString(Font, center + new Vector2(8, -cell * .45f), $"仮配置 列{_rangeCandidate!.Value.X + 1} 行{_rangeCandidate.Value.Y + 1}", HorizontalAlignment.Left, -1, 19, new Color("793f2a"));
        }
    }

    private void DrawGuideCircle(Vector2 center, float radius, Color color, int pattern)
    {
        if (!float.IsFinite(radius) || radius <= 0) return;
        if (pattern == 0)
        {
            DrawClippedGuideArc(center, radius, 0, Mathf.Tau, color, 2.5f);
            return;
        }
        var parts = pattern == 1 ? 56 : pattern == 2 ? 28 : 20;
        var fraction = pattern == 1 ? .2f : pattern == 2 ? .62f : .8f;
        for (var i = 0; i < parts; i++)
        {
            var start = Mathf.Tau * i / parts;
            var end = start + Mathf.Tau / parts * fraction;
            DrawClippedGuideArc(center, radius, start, end, color, pattern == 1 ? 2.5f : 2.2f);
        }
    }

    private void DrawClippedGuideArc(Vector2 center, float radius, float start, float end, Color color, float width)
    {
        var segments = Math.Max(2, (int)Math.Ceiling((end - start) * 24));
        var previous = center + Vector2.FromAngle(start) * radius;
        for (var i = 1; i <= segments; i++)
        {
            var current = center + Vector2.FromAngle(Mathf.Lerp(start, end, i / (float)segments)) * radius;
            var from = previous;
            var to = current;
            if (ClipGuideLine(ref from, ref to)) DrawLine(from, to, color, width, true);
            previous = current;
        }
    }

    private bool ClipGuideLine(ref Vector2 from, ref Vector2 to)
    {
        // Keep range lines inside the tactical grid, including when the selected unit is on an edge.
        var original = from;
        var delta = to - from;
        var start = 0f;
        var end = 1f;
        if (!Clip(-delta.X, original.X - _rangeClip.Position.X, ref start, ref end)
            || !Clip(delta.X, _rangeClip.End.X - original.X, ref start, ref end)
            || !Clip(-delta.Y, original.Y - _rangeClip.Position.Y, ref start, ref end)
            || !Clip(delta.Y, _rangeClip.End.Y - original.Y, ref start, ref end)) return false;
        from = original + delta * start;
        to = original + delta * end;
        return true;

        static bool Clip(float direction, float distance, ref float start, ref float end)
        {
            if (Math.Abs(direction) < .00001f) return distance >= 0;
            var ratio = distance / direction;
            if (direction < 0)
            {
                if (ratio > end) return false;
                start = Math.Max(start, ratio);
            }
            else
            {
                if (ratio < start) return false;
                end = Math.Min(end, ratio);
            }
            return true;
        }
    }

    public void HoverCellForSmoke(int x, int y)
    {
        var view = ReadView() ?? throw new InvalidOperationException("検証するマップがありません。");
        _GuiInput(new InputEventMouseMotion { Position = CellCenter(GetBoardGeometry(view), x, y) });
    }
}
