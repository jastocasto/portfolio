using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Stratum.Core;

namespace Stratum.Documents
{
  /// <summary>
  /// Per-document state, and the bridge between what the user does to Rhino
  /// objects and what has to happen to the parametric model behind them.
  ///
  /// Rhino gives us the editing verbs for free - Move, Copy, Rotate, Delete,
  /// Undo. This class listens to them and keeps the wall records honest, so a
  /// wall that is moved with the Gumball is still a wall afterwards, and a wall
  /// that is copied becomes a second real wall rather than a dead shell.
  /// </summary>
  public static class StratumDoc
  {
    static readonly Dictionary<uint, BimModel> Models = new Dictionary<uint, BimModel>();
    static readonly HashSet<Guid> PendingRebuild = new HashSet<Guid>();
    static readonly HashSet<Guid> PendingPurge = new HashSet<Guid>();
    static readonly HashSet<Guid> PendingRestore = new HashSet<Guid>();

    /// <summary>Wall records whose geometry has been deleted. Kept so that an
    /// Undo restores a real parametric wall and not an inert shell.</summary>
    static readonly Dictionary<Guid, WallDefinition> Recycled = new Dictionary<Guid, WallDefinition>();

    static bool _sweepOrphans;

    static uint _pendingDocSerial;
    static int _suspendDepth;
    static bool _idleHooked;

    /// <summary>True while Stratum itself is editing the document. Every event
    /// handler bails out during that window, so baking never feeds itself.</summary>
    public static bool IsSuspended => _suspendDepth > 0;

    public static IDisposable Suspend() => new SuspendScope();

    sealed class SuspendScope : IDisposable
    {
      public SuspendScope() { _suspendDepth++; }
      public void Dispose() { if (_suspendDepth > 0) _suspendDepth--; }
    }

    // ---- model access ------------------------------------------------------

    public static BimModel Get(RhinoDoc doc)
    {
      if (doc == null) return BimModel.CreateDefault();

      BimModel model;
      if (Models.TryGetValue(doc.RuntimeSerialNumber, out model) && model != null) return model;

      model = BimModel.CreateDefault();
      Models[doc.RuntimeSerialNumber] = model;
      return model;
    }

    public static void Set(RhinoDoc doc, BimModel model)
    {
      if (doc == null || model == null) return;
      Models[doc.RuntimeSerialNumber] = model;
      RaiseModelChanged(doc);
    }

    public static void Forget(RhinoDoc doc)
    {
      if (doc == null) return;
      Models.Remove(doc.RuntimeSerialNumber);
    }

    /// <summary>Raised whenever the model behind a document changes, so the
    /// properties panel can follow along without polling.</summary>
    public static event EventHandler<RhinoDoc> ModelChanged;

    public static void RaiseModelChanged(RhinoDoc doc)
    {
      var handler = ModelChanged;
      if (handler == null) return;
      try { handler(null, doc); } catch { /* never let a panel break the model */ }
    }

    /// <summary>Raised when the document selection changes, debounced onto idle.</summary>
    public static event EventHandler<RhinoDoc> SelectionChanged;

    static bool _selectionDirty;

    static void RaiseSelectionChanged(RhinoDoc doc)
    {
      var handler = SelectionChanged;
      if (handler == null) return;
      try { handler(null, doc); } catch { }
    }

    /// <summary>Parks a deleted wall record so Undo can bring it back intact.</summary>
    public static void Recycle(WallDefinition wall)
    {
      if (wall == null) return;
      Recycled[wall.Id] = wall;
      if (Recycled.Count > 512)
      {
        var oldest = Recycled.Keys.First();
        Recycled.Remove(oldest);
      }
    }

    static List<Guid> FindObjectsOfWall(RhinoDoc doc, Guid wallId)
    {
      var ids = new List<Guid>();
      string wanted = wallId.ToString();
      foreach (var obj in doc.Objects)
      {
        if (obj == null || obj.IsDeleted) continue;
        if (string.Equals(obj.Attributes.GetUserString(DocKeys.Wall), wanted, StringComparison.OrdinalIgnoreCase))
          ids.Add(obj.Id);
      }
      return ids;
    }

    // ---- lookups -----------------------------------------------------------

    public static WallDefinition WallOf(RhinoDoc doc, RhinoObject obj)
    {
      if (doc == null || obj == null) return null;
      var text = obj.Attributes.GetUserString(DocKeys.Wall);
      Guid id;
      if (string.IsNullOrEmpty(text) || !Guid.TryParse(text, out id)) return null;
      return Get(doc).FindWall(id);
    }

    public static int LayerIndexOf(RhinoObject obj)
    {
      if (obj == null) return -1;
      var text = obj.Attributes.GetUserString(DocKeys.LayerIndex);
      int index;
      // Written with the invariant culture in WallBaker, so read it back the same way.
      return int.TryParse(text, System.Globalization.NumberStyles.Integer,
                          System.Globalization.CultureInfo.InvariantCulture, out index) ? index : -1;
    }

    /// <summary>Walls represented in the current selection, and the individual
    /// layer indices the user has picked out inside them.</summary>
    public static List<WallDefinition> SelectedWalls(RhinoDoc doc, out HashSet<int> selectedLayerIndices)
    {
      selectedLayerIndices = new HashSet<int>();
      var walls = new List<WallDefinition>();
      if (doc == null) return walls;

      foreach (var obj in doc.Objects.GetSelectedObjects(false, false))
      {
        var wall = WallOf(doc, obj);
        if (wall == null) continue;
        if (!walls.Any(w => w.Id == wall.Id)) walls.Add(wall);
        int index = LayerIndexOf(obj);
        if (index >= 0) selectedLayerIndices.Add(index);
      }
      return walls;
    }

    // ---- event wiring ------------------------------------------------------

    public static void HookEvents()
    {
      RhinoDoc.CloseDocument += OnCloseDocument;
      RhinoDoc.NewDocument += OnNewDocument;
      RhinoDoc.BeginOpenDocument += OnBeginOpenDocument;
      RhinoDoc.DeleteRhinoObject += OnDeleteObject;
      RhinoDoc.UndeleteRhinoObject += OnUndeleteObject;
      RhinoDoc.BeforeTransformObjects += OnBeforeTransform;
      RhinoDoc.SelectObjects += OnSelectionEvent;
      RhinoDoc.DeselectObjects += OnSelectionEvent;
      RhinoDoc.DeselectAllObjects += OnDeselectAll;
    }

    public static void UnhookEvents()
    {
      RhinoDoc.CloseDocument -= OnCloseDocument;
      RhinoDoc.NewDocument -= OnNewDocument;
      RhinoDoc.BeginOpenDocument -= OnBeginOpenDocument;
      RhinoDoc.DeleteRhinoObject -= OnDeleteObject;
      RhinoDoc.UndeleteRhinoObject -= OnUndeleteObject;
      RhinoDoc.BeforeTransformObjects -= OnBeforeTransform;
      RhinoDoc.SelectObjects -= OnSelectionEvent;
      RhinoDoc.DeselectObjects -= OnSelectionEvent;
      RhinoDoc.DeselectAllObjects -= OnDeselectAll;
      UnhookIdle();
    }

    static void OnCloseDocument(object sender, DocumentEventArgs e) => Models.Remove(e.DocumentSerialNumber);
    static void OnNewDocument(object sender, DocumentEventArgs e) => Models.Remove(e.DocumentSerialNumber);
    static void OnBeginOpenDocument(object sender, DocumentOpenEventArgs e) => Models.Remove(e.DocumentSerialNumber);

    static void OnSelectionEvent(object sender, RhinoObjectSelectionEventArgs e)
    {
      _selectionDirty = true;
      _pendingDocSerial = e.Document?.RuntimeSerialNumber ?? 0;
      HookIdle();
    }

    static void OnDeselectAll(object sender, RhinoDeselectAllObjectsEventArgs e)
    {
      _selectionDirty = true;
      _pendingDocSerial = e.Document?.RuntimeSerialNumber ?? 0;
      HookIdle();
    }

    static void OnDeleteObject(object sender, RhinoObjectEventArgs e)
    {
      if (IsSuspended || e.TheObject == null) return;

      var text = e.TheObject.Attributes.GetUserString(DocKeys.Wall);
      Guid wallId;
      if (string.IsNullOrEmpty(text) || !Guid.TryParse(text, out wallId)) return;

      // Deleting any layer deletes the wall: the layers are one building element.
      PendingPurge.Add(wallId);
      _pendingDocSerial = e.TheObject.Document?.RuntimeSerialNumber ?? 0;
      HookIdle();
    }

    static void OnUndeleteObject(object sender, RhinoObjectEventArgs e)
    {
      if (IsSuspended || e.TheObject == null) return;

      var text = e.TheObject.Attributes.GetUserString(DocKeys.Wall);
      Guid wallId;
      if (string.IsNullOrEmpty(text) || !Guid.TryParse(text, out wallId)) return;

      // An undo brought the geometry back; make sure the record points at it again.
      PendingPurge.Remove(wallId);
      PendingRestore.Add(wallId);
      _pendingDocSerial = e.TheObject.Document?.RuntimeSerialNumber ?? 0;
      HookIdle();
    }

    static void OnBeforeTransform(object sender, RhinoTransformObjectsEventArgs e)
    {
      if (IsSuspended) return;

      var doc = e.Objects.Select(o => o?.Document).FirstOrDefault(d => d != null);
      if (doc == null) return;

      var model = Get(doc);
      var touched = new List<WallDefinition>();

      foreach (var obj in e.Objects)
      {
        var wall = WallOf(doc, obj);
        if (wall != null && !touched.Any(w => w.Id == wall.Id)) touched.Add(wall);
      }
      if (touched.Count == 0) return;

      _pendingDocSerial = doc.RuntimeSerialNumber;

      if (e.ObjectsWillBeCopied)
      {
        // Rhino is about to make dumb copies of the solids. Create real wall
        // records for them now; the copies themselves are replaced on idle.
        foreach (var source in touched)
        {
          var clone = source.Duplicate();
          clone.Baseline?.Transform(e.Transform);
          clone.Name = string.IsNullOrEmpty(source.Name) ? "" : source.Name;
          clone.LayerObjectIds.Clear();
          clone.GroupIndex = -1;
          model.Walls.Add(clone);
          PendingRebuild.Add(clone.Id);
        }
        _sweepOrphans = true;              // the dumb copies get replaced on idle
        HookIdle();
        return;
      }

      foreach (var wall in touched)
      {
        wall.Baseline?.Transform(e.Transform);

        var elevationPoint = new Point3d(0, 0, wall.BaseElevation);
        elevationPoint.Transform(e.Transform);
        wall.BaseElevation = elevationPoint.Z;

        // A rigid move in plan leaves Rhino's own transform of the solids exact,
        // so there is nothing to rebuild. Anything else (scale, mirror, rotate
        // out of plan) has to be regenerated from the parameters.
        if (!IsPlanarRigid(e.Transform)) PendingRebuild.Add(wall.Id);
      }

      RaiseModelChanged(doc);
      if (PendingRebuild.Count > 0) HookIdle();
    }

    static bool IsPlanarRigid(Transform xf)
    {
      var x = Vector3d.XAxis; x.Transform(xf);
      var y = Vector3d.YAxis; y.Transform(xf);
      var z = Vector3d.ZAxis; z.Transform(xf);

      const double eps = 1e-9;
      if (Math.Abs(x.Length - 1.0) > eps) return false;
      if (Math.Abs(y.Length - 1.0) > eps) return false;
      if (Math.Abs(z.Length - 1.0) > eps) return false;
      if (Math.Abs(z.X) > eps || Math.Abs(z.Y) > eps) return false;
      if (z.Z < 0) return false;                 // a mirror flips handedness
      return Math.Abs(x * y) < eps;
    }

    // ---- deferred work -----------------------------------------------------
    //
    // Rhino events fire in the middle of its own object table operations. Doing
    // the work there is how plug-ins corrupt documents, so everything is queued
    // and run once, from idle, outside any command.

    static void HookIdle()
    {
      if (_idleHooked) return;
      RhinoApp.Idle += OnIdle;
      _idleHooked = true;
    }

    static void UnhookIdle()
    {
      if (!_idleHooked) return;
      RhinoApp.Idle -= OnIdle;
      _idleHooked = false;
    }

    static void OnIdle(object sender, EventArgs e)
    {
      UnhookIdle();
      if (IsSuspended) { HookIdle(); return; }

      var doc = RhinoDoc.FromRuntimeSerialNumber(_pendingDocSerial) ?? RhinoDoc.ActiveDoc;

      bool selectionDirty = _selectionDirty;
      _selectionDirty = false;

      if (doc != null && (PendingPurge.Count > 0 || PendingRebuild.Count > 0 ||
                          PendingRestore.Count > 0 || _sweepOrphans))
      {
        var model = Get(doc);
        var undo = doc.BeginUndoRecord("Stratum update");
        try
        {
          if (PendingRestore.Count > 0)
          {
            foreach (var wallId in PendingRestore.ToList())
            {
              if (model.FindWall(wallId) != null) continue;
              WallDefinition recovered;
              if (!Recycled.TryGetValue(wallId, out recovered)) continue;

              recovered.LayerObjectIds = FindObjectsOfWall(doc, wallId);
              model.Walls.Add(recovered);
              Recycled.Remove(wallId);
            }
            PendingRestore.Clear();
          }

          if (_sweepOrphans)
          {
            using (Suspend())
            {
              foreach (var orphan in WallBaker.FindOrphans(doc, model))
                doc.Objects.Delete(orphan, true);
            }
            _sweepOrphans = false;
          }

          if (PendingPurge.Count > 0)
          {
            foreach (var wallId in PendingPurge.ToList())
            {
              var wall = model.FindWall(wallId);
              if (wall != null) WallBaker.DeleteWall(doc, model, wall);
            }
            PendingPurge.Clear();
          }

          if (PendingRebuild.Count > 0)
          {
            var walls = PendingRebuild
              .Select(id => model.FindWall(id))
              .Where(w => w != null)
              .ToList();
            PendingRebuild.Clear();

            List<string> warnings;
            WallBaker.RebuildMany(doc, model, walls, out warnings);
            foreach (var warning in warnings.Distinct().Take(5))
              RhinoApp.WriteLine("Stratum: " + warning);
          }
        }
        finally
        {
          doc.EndUndoRecord(undo);
        }

        RaiseModelChanged(doc);
        doc.Views.Redraw();
      }

      if (selectionDirty && doc != null) RaiseSelectionChanged(doc);
    }
  }
}
