using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Media.Media3D;

namespace SpaceKatMonitor
{
    internal sealed class SolidWorksViewApi : IDisposable
    {
        private readonly object _application;
        private readonly MethodInfo _commandInfo, _documentType, _activeSketch, _viewOnly, _viewHandle, _redraw;
        private readonly MethodInfo _zoom, _hasPerspective, _partBox, _assemblyBox, _rotateAboutAxis;
        private readonly PropertyInfo _activeDocument, _activeView, _transform, _transformData;
        private readonly PropertyInfo _orientation, _translation, _vectorData, _scale;
        private readonly PropertyInfo _translationArray;
        private bool? _arrayReadSupported, _arrayWriteSupported;

        private SolidWorksViewApi(Assembly interop, object application)
        {
            _application = application;
            Type app = ApiType(interop, "ISldWorks");
            Type doc = ApiType(interop, "IModelDoc2");
            Type view = ApiType(interop, "IModelView");
            _activeDocument = app.GetProperty("IActiveDoc2");
            _commandInfo = app.GetMethod("GetRunningCommandInfo");
            _documentType = doc.GetMethod("GetType");
            _activeSketch = doc.GetMethod("GetActiveSketch2");
            _viewOnly = doc.GetMethod("IsOpenedViewOnly");
            _activeView = doc.GetProperty("IActiveView");
            _redraw = doc.GetMethod("GraphicsRedraw2");
            _viewHandle = view.GetMethod("GetViewHWndx64");
            _transform = view.GetProperty("Transform");
            _transformData = ApiType(interop, "IMathTransform").GetProperty("ArrayData");
            _zoom = view.GetMethod("ZoomByFactor");
            _hasPerspective = view.GetMethod("HasPerspective");
            _rotateAboutAxis = view.GetMethod("RotateAboutAxis");
            _partBox = ApiType(interop, "IPartDoc").GetMethod("GetPartBox");
            _assemblyBox = ApiType(interop, "IAssemblyDoc").GetMethod("GetBox");
            _orientation = view.GetProperty("Orientation3");
            _translation = view.GetProperty("Translation3");
            _vectorData = ApiType(interop, "IMathVector").GetProperty("ArrayData");
            _translationArray = view.GetProperty("Translation2");
            _scale = view.GetProperty("Scale2");
        }

        internal static SolidWorksViewApi Connect(int pid, out IntPtr frame)
        {
            frame = IntPtr.Zero;
            object instance = FindInstance(pid);
            if (instance == null) return null;
            try
            {
                string applicationPath;
                using (Process process = Process.GetProcessById(pid))
                    applicationPath = process.MainModule.FileName;
                // Use the running application's own interop library. The typed interfaces
                // include the 64-bit window getters that late binding cannot invoke.
                string path = Path.Combine(Path.GetDirectoryName(applicationPath), "SolidWorks.Interop.sldworks.dll");
                Assembly interop = Assembly.LoadFrom(path);
                Type app = ApiType(interop, "ISldWorks");
                if ((int)app.GetMethod("GetProcessID").Invoke(instance, null) != pid) return null;

                object main = app.GetMethod("IFrameObject").Invoke(instance, null);
                try
                {
                    if (main == null) return null;
                    frame = new IntPtr((long)ApiType(interop, "IFrame").GetMethod("GetHWndx64").Invoke(main, null));
                    if (frame == IntPtr.Zero) return null;
                }
                finally { Release(main); }
                var connection = new SolidWorksViewApi(interop, instance);
                instance = null;
                return connection;
            }
            finally { Release(instance); }
        }

        private static object FindInstance(int pid)
        {
            IRunningObjectTable table = null;
            IBindCtx context = null;
            IEnumMoniker entries = null;
            string expected = "SolidWorks_PID_" + pid.ToString(CultureInfo.InvariantCulture);
            try
            {
                Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out table));
                Marshal.ThrowExceptionForHR(CreateBindCtx(0, out context));
                table.EnumRunning(out entries);
                var current = new IMoniker[1];
                while (entries.Next(1, current, IntPtr.Zero) == 0)
                {
                    try
                    {
                        string name;
                        try { current[0].GetDisplayName(context, null, out name); }
                        catch (COMException) { continue; }
                        if (!String.Equals(name, expected, StringComparison.OrdinalIgnoreCase)) continue;
                        object instance;
                        if (table.GetObject(current[0], out instance) == 0) return instance;
                        Release(instance);
                        break;
                    }
                    finally { Release(current[0]); }
                }
            }
            finally
            {
                Release(entries);
                Release(context);
                Release(table);
            }
            // Older releases may expose only the generic object. Connect verifies its PID.
            try { return Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException error)
            {
                if (error.ErrorCode == unchecked((int)0x800401E3)) return null;
                throw;
            }
        }

        private static Type ApiType(Assembly interop, string name)
        {
            return interop.GetType("SolidWorks.Interop.sldworks." + name, true);
        }

        internal object ActiveDocument() { return _activeDocument.GetValue(_application); }
        internal int DocumentType(object document) { return (int)_documentType.Invoke(document, null); }
        internal object ActiveView(object document) { return _activeView.GetValue(document); }
        internal IntPtr ViewHandle(object view) { return new IntPtr((long)_viewHandle.Invoke(view, null)); }

        internal bool IsCommandActive()
        {
            object[] values = { 0, null, false };
            _commandInfo.Invoke(_application, values);
            return (bool)values[2];
        }

        internal bool IsEditingSketch(object document)
        {
            object sketch = _activeSketch.Invoke(document, null);
            Release(sketch);
            return sketch != null;
        }

        internal bool IsViewOnly(object document) { return (bool)_viewOnly.Invoke(document, null); }

        internal ViewPose ReadPose(object view)
        {
            object orientation = null, translation = null, transform = null;
            try
            {
                orientation = _orientation.GetValue(view);
                translation = _translation.GetValue(view);
                transform = _transform.GetValue(view);
                var basis = orientation == null ? null : _transformData.GetValue(orientation) as double[];
                var offset = translation == null ? null : _vectorData.GetValue(translation) as double[];
                var projection = transform == null ? null : _transformData.GetValue(transform) as double[];
                double scale = (double)_scale.GetValue(view);
                if (basis == null || basis.Length != 16 || offset == null || offset.Length != 3 ||
                    projection == null || projection.Length != 16 || !Positive(scale) || !Positive(Math.Abs(projection[12])))
                    return null;

                var pose = new ViewPose(orientation, translation, basis, offset, scale, Math.Abs(projection[12]) / scale,
                    (bool)_hasPerspective.Invoke(view, null));
                orientation = translation = null;
                return pose;
            }
            finally
            {
                Release(transform);
                Release(translation);
                Release(orientation);
            }
        }

        internal void Zoom(object view, double factor) { _zoom.Invoke(view, new object[] { factor }); }

        internal double ViewScale(object view) { return (double)_scale.GetValue(view); }

        internal bool ReadZoomedPose(object view, ViewPose pose)
        {
            double scale = ViewScale(view);
            if (!Positive(scale)) return false;
            double[] offset;
            if (_translationArray != null && _translationArray.CanRead && !pose.Perspective && _arrayReadSupported != false)
            {
                double[] array = ReadTranslationArray(view);
                if (_arrayReadSupported == true && array != null)
                    offset = array;
                else
                {
                    offset = ReadTranslation(view);
                    // Compare with the current documented API before using the legacy array.
                    _arrayReadSupported = SameOffset(array, offset);
                }
            }
            else
                offset = ReadTranslation(view);
            if (offset == null) return false;
            Array.Copy(offset, pose.Offset, 3);
            pose.Scale = scale;
            return true;
        }

        private double[] ReadTranslation(object view)
        {
            object translation = _translation.GetValue(view);
            try
            {
                return translation == null ? null : ValidOffset(_vectorData.GetValue(translation));
            }
            finally { Release(translation); }
        }

        private double[] ReadTranslationArray(object view)
        {
            try { return ValidOffset(_translationArray.GetValue(view)); }
            catch (TargetInvocationException error)
            {
                if (!ArrayAccessUnavailable(error)) throw;
                return null;
            }
        }

        private bool WriteTranslationArray(object view, double[] offset)
        {
            // Confirm the first observable change. A no-op cannot prove that the setter works.
            if (!_arrayWriteSupported.HasValue && SameOffset(ReadTranslation(view), offset)) return true;
            try { _translationArray.SetValue(view, offset); }
            catch (TargetInvocationException error)
            {
                if (!ArrayAccessUnavailable(error)) throw;
                _arrayWriteSupported = false;
                return false;
            }
            if (!_arrayWriteSupported.HasValue)
                _arrayWriteSupported = SameOffset(ReadTranslation(view), offset);
            return _arrayWriteSupported.Value;
        }

        private static double[] ValidOffset(object value)
        {
            double[] offset = value as double[];
            if (offset == null || offset.Length != 3) return null;
            foreach (double component in offset)
                if (Double.IsNaN(component) || Double.IsInfinity(component)) return null;
            return offset;
        }

        private static bool SameOffset(double[] first, double[] second)
        {
            if (first == null || second == null) return false;
            for (int i = 0; i < 3; i++)
                if (Math.Abs(first[i] - second[i]) > 1e-9) return false;
            return true;
        }

        private static bool ArrayAccessUnavailable(TargetInvocationException error)
        {
            Exception cause = error.GetBaseException();
            if (cause is NotImplementedException || cause is NotSupportedException) return true;
            var com = cause as COMException;
            if (com == null) return false;
            const int NotImplemented = unchecked((int)0x80004001);
            const int NoInterface = unchecked((int)0x80004002);
            const int MemberNotFound = unchecked((int)0x80020003);
            const int InvalidArgument = unchecked((int)0x80070057);
            return com.ErrorCode == NotImplemented || com.ErrorCode == NoInterface ||
                com.ErrorCode == MemberNotFound || com.ErrorCode == InvalidArgument;
        }

        internal double[] ModelBounds(object document, int documentType)
        {
            return documentType == 1 ? _partBox.Invoke(document, new object[] { true }) as double[] :
                _assemblyBox.Invoke(document, new object[] { 0 }) as double[];
        }

        internal bool WritePose(object view, ViewPose pose, bool rotated, Func<bool> canMove)
        {
            if (rotated) _transformData.SetValue(pose.Orientation, pose.Basis);
            _vectorData.SetValue(pose.Translation, pose.Offset);
            if (!canMove()) return false;

            if (rotated) _orientation.SetValue(view, pose.Orientation);
            _translation.SetValue(view, pose.Translation);
            return true;
        }

        internal bool WriteRotation(object view, ViewPose pose, Vector3D radians, Vector3D pivotInView,
            Func<bool> canMove)
        {
            if (_rotateAboutAxis == null || pose.Perspective)
            {
                RotateViewTransform(pose.Basis, pose.Offset, radians, pivotInView);
                return WritePose(view, pose, true, canMove);
            }

            double angle = radians.Length;
            Vector3D axis = ToModelVector(pose.Basis, radians / angle);
            var offset = new Vector3D(pose.Offset[0], pose.Offset[1], pose.Offset[2]);
            Vector3D pivot = ToModelVector(pose.Basis, (pivotInView - offset) / pose.Scale);

            // The native call matches the orientation. Write the matching translation to keep the orbit center fixed.
            RotateViewTransform(pose.Basis, pose.Offset, radians, pivotInView);
            bool writeArray = _arrayReadSupported == true && _translationArray.CanWrite && _arrayWriteSupported != false;
            if (!writeArray) _vectorData.SetValue(pose.Translation, pose.Offset);
            if (!canMove()) return false;
            _rotateAboutAxis.Invoke(view, new object[] { angle, pivot.X, pivot.Y, pivot.Z, axis.X, axis.Y, axis.Z });
            if (writeArray)
            {
                if (WriteTranslationArray(view, pose.Offset)) return true;
                _vectorData.SetValue(pose.Translation, pose.Offset);
            }
            _translation.SetValue(view, pose.Translation);
            return true;
        }

        private static Vector3D ToModelVector(double[] basis, Vector3D viewVector)
        {
            // The orientation is orthonormal, so its transpose maps view vectors into model space.
            return new Vector3D(
                basis[0] * viewVector.X + basis[1] * viewVector.Y + basis[2] * viewVector.Z,
                basis[3] * viewVector.X + basis[4] * viewVector.Y + basis[5] * viewVector.Z,
                basis[6] * viewVector.X + basis[7] * viewVector.Y + basis[8] * viewVector.Z);
        }

        internal static void RotateViewTransform(double[] basis, double[] offset, Vector3D radians, Vector3D pivotInView)
        {
            // SOLIDWORKS uses row vectors. Append the incremental rotation in view space.
            Matrix3D rotation = Matrix3D.Identity;
            rotation.Rotate(new Quaternion(radians, radians.Length * (180 / Math.PI)));
            for (int row = 0; row < 9; row += 3)
            {
                Vector3D axis = rotation.Transform(new Vector3D(basis[row], basis[row + 1], basis[row + 2]));
                basis[row] = axis.X;
                basis[row + 1] = axis.Y;
                basis[row + 2] = axis.Z;
            }

            Vector3D translation = new Vector3D(offset[0], offset[1], offset[2]);
            Vector3D centerOffset = pivotInView + rotation.Transform(translation - pivotInView);
            offset[0] = centerOffset.X;
            offset[1] = centerOffset.Y;
            offset[2] = centerOffset.Z;
        }

        internal static Vector3D Project(double[] basis, Vector3D point)
        {
            return new Vector3D(point.X * basis[0] + point.Y * basis[3] + point.Z * basis[6],
                point.X * basis[1] + point.Y * basis[4] + point.Z * basis[7],
                point.X * basis[2] + point.Y * basis[5] + point.Z * basis[8]);
        }

        internal sealed class ViewPose : IDisposable
        {
            internal readonly object Orientation, Translation;
            internal readonly double[] Basis, Offset;
            internal readonly double ViewUnitsToPixels;
            internal readonly bool Perspective;
            internal double Scale;

            internal ViewPose(object orientation, object translation, double[] basis, double[] offset,
                double scale, double viewUnitsToPixels, bool perspective)
            {
                Orientation = orientation;
                Translation = translation;
                Basis = basis;
                Offset = offset;
                Scale = scale;
                ViewUnitsToPixels = viewUnitsToPixels;
                Perspective = perspective;
            }

            internal Vector3D RotationCenter(double[] bounds, int width, int height)
            {
                if (bounds == null || bounds.Length != 6) return new Vector3D();
                foreach (double value in bounds)
                    if (Double.IsNaN(value) || Double.IsInfinity(value)) return new Vector3D();
                Vector3D center = new Vector3D((bounds[0] + bounds[3]) * .5, (bounds[1] + bounds[4]) * .5,
                    (bounds[2] + bounds[5]) * .5);
                Vector3D inView = Project(Basis, center) * Scale + new Vector3D(Offset[0], Offset[1], Offset[2]);
                if (Perspective) return inView;

                // Use the projected model center while the model fits on screen. When zoomed
                // in, keep the viewport center fixed at the model's depth instead.
                double x = Math.Abs(bounds[3] - bounds[0]) * .5;
                double y = Math.Abs(bounds[4] - bounds[1]) * .5;
                double z = Math.Abs(bounds[5] - bounds[2]) * .5;
                double halfWidth = (x * Math.Abs(Basis[0]) + y * Math.Abs(Basis[3]) + z * Math.Abs(Basis[6])) * Scale;
                double halfHeight = (x * Math.Abs(Basis[1]) + y * Math.Abs(Basis[4]) + z * Math.Abs(Basis[7])) * Scale;
                bool fits = (Math.Abs(inView.X) + halfWidth) * ViewUnitsToPixels <= width * .5 &&
                    (Math.Abs(inView.Y) + halfHeight) * ViewUnitsToPixels <= height * .5;
                return fits ? inView : new Vector3D(0, 0, inView.Z);
            }

            internal void Pan(double x, double y)
            {
                Offset[0] += x;
                Offset[1] += y;
            }

            public void Dispose()
            {
                Release(Translation);
                Release(Orientation);
            }
        }

        private static bool Positive(double value) { return value > 1e-9 && !Double.IsInfinity(value); }

        internal void Redraw(object doc) { _redraw.Invoke(doc, null); }

        public void Dispose() { Release(_application); }

        internal static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        [DllImport("ole32.dll")] private static extern int GetRunningObjectTable(uint reserved, out IRunningObjectTable table);
        [DllImport("ole32.dll")] private static extern int CreateBindCtx(uint reserved, out IBindCtx context);
    }
}
