using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace LucidControl
{
    public class LucidControlInfo : GH_AssemblyInfo
    {
        public override string Name => "LucidControl";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "";

        public override Guid Id => new Guid("72afb492-603b-47bf-be1f-9eb799cb9af9");

        //Return a string identifying you or your company.
        public override string AuthorName => "";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "";
    }
}