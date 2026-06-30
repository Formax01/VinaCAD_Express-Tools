using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Teigha.Runtime;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace SamplesCsharp
{
  public class SampleCode
  {
    Document docActive => Prima.VinaCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
    public void AddCircle()
    {
      // Get the current document and database
      Document acDoc = Application.DocumentManager.MdiActiveDocument;
      Editor ed = acDoc.Editor;
      Database acCurDb = acDoc.Database;

      // Start a transaction
      using (Transaction acTrans = acCurDb.TransactionManager.StartTransaction())
      {
        // Open the Block table for read
        BlockTable acBlkTbl;
        acBlkTbl = acTrans.GetObject(acCurDb.BlockTableId,
                                     OpenMode.ForRead) as BlockTable;

        // Open the Block table record Model space for write
        BlockTableRecord acBlkTblRec;
        acBlkTblRec = acTrans.GetObject(acBlkTbl[BlockTableRecord.ModelSpace],
                                        OpenMode.ForWrite) as BlockTableRecord;

        // Ask user to specify the center point of circle
        string getCenterMessage = "Specify the center point of new Circle";
        PromptPointOptions centerOption = new PromptPointOptions(getCenterMessage);
        PromptPointResult centerResult = ed.GetPoint(centerOption);

        if (centerResult.Status != PromptStatus.OK)
        {
          return;
        }

        // Ask user to specify the radius of circle
        string getRadiusMessage = "Specify the radius of new Circle";
        PromptPointOptions radiusOption = new PromptPointOptions(getRadiusMessage)
        {
          BasePoint = centerResult.Value,
          UseBasePoint = true,
        };
        PromptPointResult radiusResult = ed.GetPoint(radiusOption);

        if (radiusResult.Status != PromptStatus.OK)
        {
          return;
        }

        // Create a circle
        Circle acCirc = new Circle();

        //Set data of circle by specified data
        Point3d center = centerResult.Value;
        acCirc.Center = center;
        double radius = center.DistanceTo(radiusResult.Value);
        acCirc.Radius = radius;

        // Add the new object to the block table record and the transaction
        acBlkTblRec.AppendEntity(acCirc);
        acTrans.AddNewlyCreatedDBObject(acCirc, true);

        // Save the new object to the database
        acTrans.Commit();
      }
    }
    class EllipseJig : EntityJig
        {
            Point3d mCenterPt, mAxisPt, acquiredPoint;
            Vector3d mNormal, mMajorAxis;
            double mRadiusRatio;

            int mPromptCounter;

            DynamicDimensionDataCollection m_dims;

            public EllipseJig(Point3d center, Vector3d vec) : base(new Ellipse())
            {
                mCenterPt = center;
                mNormal = vec;
                mRadiusRatio = 0.00001;
                mPromptCounter = 0;

                m_dims = new DynamicDimensionDataCollection();
                Dimension dim1 = new AlignedDimension();
                //dim1.SetDatabaseDefaults();
                m_dims.Add(new DynamicDimensionData(dim1, true, true));
                dim1.DynamicDimension = true;
                Dimension dim2 = new AlignedDimension();
                //dim2.SetDatabaseDefaults();
                m_dims.Add(new DynamicDimensionData(dim2, true, true));
                dim2.DynamicDimension = true;

                Entity.SetDatabaseDefaults();

            }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                JigPromptPointOptions jigOpts = new JigPromptPointOptions();
                jigOpts.UserInputControls = (UserInputControls.Accept3dCoordinates | UserInputControls.NoZeroResponseAccepted | UserInputControls.NoNegativeResponseAccepted);

                if (mPromptCounter == 0)
                {
                    jigOpts.Message = "\nEllipse Major axis:";
                    jigOpts.Keywords.Add("E", "Exit", "Exit");
                    jigOpts.Keywords.Add("T", "Test", "Test");
                    PromptPointResult dres = prompts.AcquirePoint(jigOpts);
                    Point3d axisPointTemp = dres.Value;
                    if (axisPointTemp != mAxisPt)
                    {
                        mAxisPt = axisPointTemp;
                    }

                    if (dres.Status == PromptStatus.Cancel)
                    {
                        return SamplerStatus.Cancel;
                    }
                    else
                        return SamplerStatus.OK;


                }
                else if (mPromptCounter == 1)
                {
                    jigOpts.BasePoint = mCenterPt;
                    jigOpts.UseBasePoint = true;
                    jigOpts.Message = "\nEllipse Minor axis:";
                    jigOpts.Keywords.Add("E2", "Exit2", "Exit2");;
                    double radiusRatioTemp = -1;
                    PromptPointResult res = prompts.AcquirePoint(jigOpts);
                    acquiredPoint = res.Value;
                    radiusRatioTemp = mCenterPt.DistanceTo(acquiredPoint);

                    if (radiusRatioTemp != mRadiusRatio)
                        mRadiusRatio = radiusRatioTemp;
                    else
                        return SamplerStatus.NoChange;

                    if (res.Status == PromptStatus.Cancel)
                        return SamplerStatus.Cancel;
                    else
                        return SamplerStatus.OK;

                }
                else
                {
                    return SamplerStatus.NoChange;
                }


            }
            protected override bool Update()
            {
                double radiusRatio = 1.0;
                switch (mPromptCounter)
                {
                    case 0:
                        mMajorAxis = mAxisPt - mCenterPt;
                        break;
                    case 1:
                        radiusRatio = mRadiusRatio / mMajorAxis.Length;
                        break;
                }

                try
                {
                    ((Ellipse)Entity).Set(mCenterPt, new Vector3d(0, 0, 1), mMajorAxis, radiusRatio, 0.0, 6.28318530717958647692);
                }
                catch (System.Exception)
                {
                    return false;
                }

                return true;

            }
            protected override DynamicDimensionDataCollection GetDynamicDimensionData(double dimScale)
            {
                return m_dims;
            }

            void UpdateDimensions()
            {
                if (mPromptCounter == 0)
                {
                    Ellipse myellipse = (Ellipse)Entity;
                    AlignedDimension dim = (AlignedDimension)m_dims[0].Dimension;
                    dim.XLine1Point = myellipse.Center;
                    dim.XLine2Point = mAxisPt;
                    dim.DimLinePoint = myellipse.Center;
                }
                else
                {
                    Ellipse myellipse = (Ellipse)Entity;
                    AlignedDimension dim2 = (AlignedDimension)m_dims[1].Dimension;
                    dim2.XLine1Point = myellipse.Center;
                    dim2.XLine2Point = acquiredPoint;
                    dim2.DimLinePoint = myellipse.Center;
                }

            }
            public void setPromptCounter(int i)
            {
                mPromptCounter = i;
            }
            public Entity GetEntity()
            {
                return Entity;
            }

        }
    public void Test_EntityJig()
    {
        Editor ed = docActive.Editor;
        PromptPointOptions opts = new PromptPointOptions("\nEnter Ellipse Center Point:");
        PromptPointResult res = ed.GetPoint(opts);

        Vector3d x = docActive.Database.Ucsxdir;
        Vector3d y = docActive.Database.Ucsydir;
        Vector3d NormalVec = x.CrossProduct(y);


        Database db = docActive.Database;
        var tm = db.TransactionManager;

        EllipseJig jig = new EllipseJig(res.Value, NormalVec.GetNormal());
        //first call drag to get the major axis
        jig.setPromptCounter(0);
        var result = (PromptPointResult)docActive.Editor.Drag(jig);
        // Again call drag to get minor axis					
        jig.setPromptCounter(1);
        var result2 = (PromptPointResult)docActive.Editor.Drag(jig);

        //Append entity.
        using (Transaction myT = tm.StartTransaction())
        {
            BlockTable bt = (BlockTable)tm.GetObject(db.BlockTableId, OpenMode.ForRead, false);
            BlockTableRecord btr = (BlockTableRecord)tm.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite, false);
            btr.AppendEntity(jig.GetEntity());
            tm.AddNewlyCreatedDBObject(jig.GetEntity(), true);
            myT.Commit();
        }


    }
    }
}
