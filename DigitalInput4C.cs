using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using Grasshopper.Kernel.Types;
using Color = System.Drawing.Color;
using Grasshopper.Kernel.Parameters;
using System.Linq;
using lucidio;
using System.Threading.Tasks;
using System.Threading;
using System.Xml;

namespace LucidControl
{
    public class DigitalInput4C : GH_Component
    {

        string lastPortname = "";
        List<bool> lastChannels = new List<bool>();

        List<string> deviceInfo;

        LucidControlDI4 di4;

        Task readingTask;
        CancellationTokenSource tokenSource;
        CancellationToken token;
        delegate void NoArgDelegate();


        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public DigitalInput4C()
          : base("LucidControl DI4", "DI4",
            "Receive digital signals from a LucidControl DI4 device",
            "Params", "Input")
        {
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.LucidControlIcon;

        public override Guid ComponentGuid => new Guid("72371d06-eaff-440c-8ae0-85ae919e3788");



        struct ReadResult
        {
            public ReadResult(bool[] values, IoReturn error)
            {
                this.values = values;
                this.error = error;
            }
            public bool[] values;
            public IoReturn error;
        }

        Mutex mutex = new Mutex();
        ReadResult readResult;

        // ReadingLoop 
        protected void ReadingLoop()
        {
            ValueDI1[] values = new ValueDI1[4];
            NoArgDelegate expireSolutionFn = () => { this.ExpireSolution(true); };
            bool[] channelsToRead = lastChannels.ToArray();
            bool[] lastValues = null;
            IoReturn readState;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    readState = di4.GetIoGroup(channelsToRead, values);
                }
                catch
                {
                    readState = IoReturn.IO_RETURN_ERR_EXEC;
                    mutex.WaitOne();
                    readResult = new ReadResult(null, readState);
                    mutex.ReleaseMutex();
                    Rhino.RhinoApp.InvokeOnUiThread(expireSolutionFn);
                    return;
                }

                bool[] valuesAsBool = new bool[4] { values[0].GetValue(), values[1].GetValue(), values[2].GetValue(), values[3].GetValue() };
                if (lastValues != null && valuesAsBool.SequenceEqual(lastValues))
                    continue;

                lastValues = valuesAsBool;
                mutex.WaitOne();
                readResult = new ReadResult(valuesAsBool, readState);
                mutex.ReleaseMutex();
                Rhino.RhinoApp.InvokeOnUiThread(expireSolutionFn);
            }
        }

        // ErrorState extends IoReturn from LucidControl to also handle exceptions and invalid states.
        protected enum ErrorState
        {
            IO_RETURN_OK = IoReturn.IO_RETURN_OK,
            IO_RETURN_NSUP = IoReturn.IO_RETURN_NSUP,
            IO_RETURN_INV_LENGTH = IoReturn.IO_RETURN_INV_LENGTH,
            IO_RETURN_INV_P1 = IoReturn.IO_RETURN_INV_P1,
            IO_RETURN_INV_P2 = IoReturn.IO_RETURN_INV_P2,
            IO_RETURN_INV_IOCH = IoReturn.IO_RETURN_INV_IOCH,
            IO_RETURN_INV_VALUE = IoReturn.IO_RETURN_INV_VALUE,
            IO_RETURN_INV_PARAM = IoReturn.IO_RETURN_INV_PARAM,
            IO_RETURN_INV_DATA = IoReturn.IO_RETURN_INV_DATA,
            IO_RETURN_ERR_EXEC = IoReturn.IO_RETURN_ERR_EXEC,
            IO_RETURN_ERR_INTERNAL = IoReturn.IO_RETURN_ERR_INTERNAL,

            ER_PORT_ALREADY_OPENED = 1 << 9,
            ER_ERROR_OPEN_PORT = 1 << 10,
        };

        protected ErrorState InitLucidControl(string portname, bool[] channels)
        {
            di4 = new LucidControlDI4(portname);
            try
            {
                if (!di4.Open())
                {
                    return ErrorState.ER_PORT_ALREADY_OPENED;
                }
            }
            catch
            {
                di4.Close();
                return ErrorState.ER_ERROR_OPEN_PORT;
            }

            // Initialize all inputs in Reflect Mode
            IoReturn ret = IoReturn.IO_RETURN_OK;
            for (byte i = 0; (i < 4) && ret == IoReturn.IO_RETURN_OK; i++)
                ret = di4.SetParamMode(i, false, channels[i] ? LucidControlDI4.LCDIMode.REFLECT_VALUE : LucidControlDI4.LCDIMode.INACTIVE);

            if (ret != IoReturn.IO_RETURN_OK)
            {
                di4.Close();
                return (ErrorState)ret;
            }

            ret = IoReturn.IO_RETURN_OK;
            for (byte i = 0; (i < 4) && ret == IoReturn.IO_RETURN_OK; i++)
                ret = di4.SetParamScanTime(i, false, 50000);

            if (ret != IoReturn.IO_RETURN_OK)
            {
                di4.Close();
                return (ErrorState)ret;
            }

            return ErrorState.IO_RETURN_OK;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Active", "A", "Set to true to turn device active", GH_ParamAccess.item, false);
            pManager.AddTextParameter("Port", "P", "The port name of the connected LucidDevice", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Channels", "C", "Set each port to true that should be read", GH_ParamAccess.list, new List<bool> { true, true, true, true });
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBooleanParameter("State", "S", "The states of the ports", GH_ParamAccess.list);
            pManager.AddTextParameter("DeviceInfo", "I", "Device information gathered from device.", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool isActive = false;
            string portname = "";
            List<bool> channels = new List<bool>();

            if (!DA.GetData(0, ref isActive)) return;
            if (!DA.GetData(1, ref portname)) return;
            if (!DA.GetDataList(2, channels)) return;

            mutex.WaitOne();
            IoReturn readState = readResult.error;
            bool[] readValues = readResult.values;
            mutex.ReleaseMutex();

            bool doInit = false;
            bool doClose = !isActive;

            if (channels.Count != 4)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Exactly four channel values required");
                doClose = true;
                isActive = false;
            }

            if (portname != lastPortname || !channels.SequenceEqual(lastChannels))
            {
                lastChannels = channels;

                lastPortname = portname;
                doClose = true;
                doInit = isActive;
            }

            if (readState != IoReturn.IO_RETURN_OK)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error: " + readState.ToString());
                doClose = true;
                doInit = isActive;
            }

            doInit |= isActive && (di4 == null || !di4.IsOpened());
            doClose &= (di4 != null && di4.IsOpened());

            if (doClose)
            {
                tokenSource.Cancel();
                readingTask.Wait(); // Check if I need to wait here
                di4.Close();
            }

            if (doInit)
            {
                ErrorState err = InitLucidControl(portname, lastChannels.ToArray());
                if (err != ErrorState.IO_RETURN_OK)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error: " + err.ToString());
                    return;
                }

                deviceInfo = new List<string>();
                di4.Identify(1);
                deviceInfo.Add("Class name: " + di4.GetDeviceClassName());
                deviceInfo.Add("Type name: " + di4.GetDeviceTypeName());
                deviceInfo.Add("Firmware revision: " + di4.GetRevisionFw());
                deviceInfo.Add("Hardware revision: " + di4.GetRevisionHw());
                deviceInfo.Add("Serial number: " + di4.GetDeviceSnr());


                tokenSource = new CancellationTokenSource();
                token = tokenSource.Token;
                readingTask = Task.Factory.StartNew(ReadingLoop, token);
                return;
            }




            if (!isActive)
                return;


            DA.SetDataList(0, readValues);
            DA.SetDataList(1, deviceInfo);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            base.RemovedFromDocument(document);

            if (tokenSource == null)
                return;
            tokenSource.Cancel();
            readingTask.Wait(); // TODO Check if I need to wait here
            di4.Close();

        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            base.DocumentContextChanged(document, context);

            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
            {
                if (tokenSource == null)
                    return;
                tokenSource.Cancel();
                readingTask.Wait(); // TODO Check if I need to wait here
                di4.Close();
            }
        }


    }
}
