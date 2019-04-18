using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
/*测试完毕*/
namespace marinefeaturesWebAPI.Controllers
{
    public class MarineFeaturesController : ApiController
    {
        private double[] resultWindu;
        private double[] resultWindv;
        private float[] resultCurrentu;
        private float[] resultCurrentv;
        private float [] resultOtherFeatures;//风、流之外的其他要素响应结果数组
        private double[] resultlon;
        private double[] resultlat;
       
        private double[] dotX;
        private double[] dotY;//为了配合读出的nc文件value值而创建的经纬度数组（笨方法）
       
        private double[] newdotX;
        private double[] newdotY;//为了配合读出的nc文件value值而创建的经纬度数组（笨方法）
        private double[] windSpeed;
        private double [] currentSpeed;
        private int index;

        private void cvtWebMercator(double lon, double lat, out double x, out double y)//实现经纬度坐标向webM投影坐标的转换
        {
            double earthRad = 6378137.0;
            x = lon * Math.PI / 180 * earthRad;
            double a = lat * Math.PI / 180;
            y = earthRad / 2 * Math.Log((1.0 + Math.Sin(a)) / (1.0 - Math.Sin(a)));
        }
  
        private void webMercator2LngLat(double x, double y, out double lon, out double lat)//webM投影坐标向经纬度的转换
        {
            lon = x / 20037508.34 * 180;
            lat = y / 20037508.34 * 180;
            lat = 180 / Math.PI * (2 * Math.Atan(Math.Exp(lat * Math.PI / 180)) - Math.PI / 2);

        }



        [HttpPost]
        public JObject selectedJsonCreation(string date,string featureType, double wlon, double elon, double slat, double nlat)
        {
            readNC(date,featureType,wlon,elon,slat,nlat);
           
            if (featureType == "wind" || featureType == "current")
            {
                return createwindorcurrentJSON(newdotX,newdotY,featureType);
            }
            else {
                return createotherfeaturesJSON(newdotX,newdotY,featureType);
            }
 
        }
        private JObject createwindorcurrentJSON(double[] Lon, double[] Lat,string featureType)
        {
            JObject windField = new JObject();
            windField.Add(new JProperty("type", "FeatureCollection"));
            JArray Features = new JArray();
            if (featureType == "wind")
            {
                for (int i = 0; i < index; i++)
                {
                    windSpeed[i] = Math.Sqrt(Math.Pow(resultWindu[i], 2) + Math.Pow(resultWindv[i], 2));//得到风速
                }
         
                for (int i = 0; i < index; i++)
                {
                    JArray crs = new JArray(new JValue(Lon[i]), new JValue(Lat[i]));

                    JObject geometry = new JObject();
                    geometry.Add(new JProperty("type", "Point"));
                    geometry.Add(new JProperty("coordinates", crs));
                    JObject speed = new JObject();
                    speed.Add(new JProperty("u", resultWindu[i]));
                    speed.Add(new JProperty("v", resultWindv[i]));
                    speed.Add(new JProperty(featureType + "Speed", windSpeed[i]));
                    JObject feature = new JObject();
                    feature.Add(new JProperty("type", "Feature"));
                    feature.Add(new JProperty("geometry", geometry));
                    feature.Add(new JProperty("properties", speed));
                    Features.Add(feature);
                }
                windField.Add(new JProperty("features", Features));//将数据组织为geojson
            }
            else {
                for (int i = 0; i < index; i++)
                {
                    currentSpeed[i] = Math.Sqrt(Math.Pow(resultCurrentu[i], 2) + Math.Pow(resultCurrentv[i], 2));//得到风速
                }
                for (int i = 0; i < index; i++)
                {
                    JArray crs = new JArray(new JValue(Lon[i]), new JValue(Lat[i]));

                    JObject geometry = new JObject();
                    geometry.Add(new JProperty("type", "Point"));
                    geometry.Add(new JProperty("coordinates", crs));
                    JObject speed = new JObject();
                    speed.Add(new JProperty("u", resultCurrentu[i]));
                    speed.Add(new JProperty("v", resultCurrentv[i]));
                    speed.Add(new JProperty(featureType + "Speed", currentSpeed[i]));
                    JObject feature = new JObject();
                    feature.Add(new JProperty("type", "Feature"));
                    feature.Add(new JProperty("geometry", geometry));
                    feature.Add(new JProperty("properties", speed));
                    Features.Add(feature);
                }
                windField.Add(new JProperty("features", Features));//将数据组织为geojson
            }
           
            
            return windField;
        }
        private JObject createotherfeaturesJSON(double[] Lon, double[] Lat,string featureType)
        {
            JObject windField = new JObject();
            windField.Add(new JProperty("type", "FeatureCollection"));
            JArray Features = new JArray();
            for (int i = 0; i < index; i++)
            {
                JArray crs = new JArray(new JValue(Lon[i]), new JValue(Lat[i]));
                JObject geometry = new JObject();
                geometry.Add(new JProperty("type", "Point"));
                geometry.Add(new JProperty("coordinates", crs));
                JObject value = new JObject();
                value.Add(new JProperty(featureType, resultOtherFeatures[i]));
                JObject feature = new JObject();
                feature.Add(new JProperty("type", "Feature"));
                feature.Add(new JProperty("geometry", geometry));
                feature.Add(new JProperty("properties", value));
                Features.Add(feature);
            }
            windField.Add(new JProperty("features", Features));//将数据组织为geojson
            return windField;
        }

        private void readNC(string date,string featureType,double wlon,double elon,double slat,double nlat)
        {
            
            //字符串拼接
            Dictionary<string, string> marinefeatureDic = new Dictionary<string, string>() {
                { "chl", "//CHL_global_9km_"+date+ "_20161010.nc" },
                { "current", "//OceanCurrent_global_250_" +date+ "_20160927.nc" },
                { "sea_surface_air_temperature","//Sea_surface_air_temperature_global_250_"+date+"_20160821.nc"},
                { "sst","//SST_global_0.1degree_"+date+"_20160905.nc"},
                { "swh","//wave_global_250_"+date+"_20170510.nc"},
                { "water_vapor","//Watervapor_global_250_"+date+"_20161017.nc"},
                { "wind","//wind_global_250_"+date+"_20161216.nc"}
            };

            int ncid, dimidLon, dimidLat, lonLen, latLen,varId1,varId2;
            string rootpath = System.Web.Hosting.HostingEnvironment.MapPath("~/");
            string ncPath = rootpath + "App_Data//MarineFeaturesData//" + featureType+ marinefeatureDic[featureType];

            int num = NetCDF.nc_open(ncPath, NetCDF.CreateMode.NC_NOWRITE, out ncid);

           
            string dimLon = "lon";
            string dimLat = "lat";
            
            string varLon = "lon";
            string varLat = "lat";
            string varValue1, varValue2;

            NetCDF.nc_inq_dimid(ncid, dimLon, out dimidLon);
            NetCDF.nc_inq_dimid(ncid, dimLat, out dimidLat);
            NetCDF.nc_inq_dimlen(ncid, dimidLon, out lonLen);
            NetCDF.nc_inq_dimlen(ncid, dimidLat, out latLen);

            int[] origin = new int[] { 0, 0 };
            int[] size = new int[] { lonLen, latLen };
            int[] originLon = new int[] { 0 };
            int[] originLat = new int[] { 0 };
            int[] sizeLon = new int[] { lonLen };
            int[] sizeLat = new int[] { latLen };
            resultlon = new double[lonLen];
            resultlat = new double[latLen];

            //通用变量经度、纬度的变量ID
            int varidlon = NetCDF.GetVarId(ncid, varLon);
            int varidlat = NetCDF.GetVarId(ncid, varLat);

            //读出通用变量值分别存入resultlon、resultlat两个数组中
            NetCDF.nc_get_vara_double(ncid, varidlon, originLon, sizeLon, resultlon);
            NetCDF.nc_get_vara_double(ncid, varidlat, originLat, sizeLat, resultlat);

            //判断用户请求要素类型，以定义与请求一致的变量
            if (featureType == "wind" || featureType == "current")
            {
                varValue1 = "u";
                varValue2 = "v";
                varId1 = NetCDF.GetVarId(ncid, varValue1);
                varId2 = NetCDF.GetVarId(ncid, varValue2);
                windorcurrentR(featureType, lonLen, latLen,ncid,varId1,varId2, wlon, elon, slat, nlat);
            }
            else {
               // varValue1 = featureType.ToUpper();
                varId1 = NetCDF.GetVarId(ncid, featureType);
                otherfeaturesR(featureType, lonLen, latLen,ncid,varId1,wlon,elon,slat,nlat);

            }
  
        }
        private void windorcurrentR(string featureType ,int lonLen,int latLen, int ncid, int varidu, int varidv,double wlon, double elon, double slat, double nlat) {
            index = 0;

            double[] fileWindu = new double[latLen * lonLen];
            double[] fileWindv = new double[latLen * lonLen];//完整的一个NC文件中的u，v风速

            //int[] fileCurrentu = new int[latLen * lonLen];
            //int[] fileCurrentv = new int[latLen * lonLen];//完整的一个NC文件中的u，v风速
            float[] fileCurrentu = new float[latLen * lonLen];
            float[] fileCurrentv = new float[latLen * lonLen];//完整的一个NC文件中的u，v风速
            windSpeed = new double[latLen * lonLen];//风速
            currentSpeed = new double[latLen * lonLen];//风速
            int[] origin = new int[] { 0, 0 };
            int[] size = new int[] { lonLen, latLen };
            

            dotX = new double[latLen * lonLen];
            dotY = new double[latLen * lonLen];
         
            newdotX = new double[latLen * lonLen];
            newdotY = new double[latLen * lonLen];
            resultWindu = new double[latLen * lonLen];
            resultWindv = new double[latLen * lonLen];
            resultCurrentu = new float[latLen * lonLen];
            resultCurrentv = new float[latLen * lonLen];
            if (featureType == "wind")
            {
                //NetCDF.nc_get_vara_double(ncid, varidu, origin, size, fileWindu);
                //NetCDF.nc_get_vara_double(ncid, varidv, origin, size, fileWindv);
                NetCDF.nc_get_var_double(ncid, varidu, fileWindu);
                NetCDF.nc_get_var_double(ncid, varidv,  fileWindv);
                for (int i = 0; i < lonLen; i++)//此循环将51*51格网内起始坐标按行进行组织成一维数组便于进一步的计算
                {
                    resultlat.CopyTo(dotY, resultlat.Length * i);
                    for (int j = i * latLen; j < (i + 1) * latLen; j++)
                    {
                        dotX[j] = resultlon[i];
                    }
                }
                for (int i = 0; i < latLen * lonLen; i++)
                {
                    if ((dotX[i] >= wlon && dotX[i] <= elon) && (dotY[i] >= slat && dotY[i] <= nlat))
                    {
                        newdotX[index] = dotX[i];
                        newdotY[index] = dotY[i];
                        resultWindu[index] = fileWindu[i];
                        resultWindv[index] = fileWindv[i];
                        index++;
                    }
                }
            }
            else {
                NetCDF.nc_get_var_float(ncid, varidu,  fileCurrentu);
                NetCDF.nc_get_var_float(ncid, varidv,  fileCurrentv);
                //NetCDF.nc_get_var_int(ncid, varidu, fileCurrentu);
                for (int i = 0; i < latLen; i++)//此循环将51*51格网内起始坐标按行进行组织成一维数组便于进一步的计算
                {
                    resultlon.CopyTo(dotX, resultlon.Length * i);
                    for (int j = i * lonLen; j < (i + 1) * lonLen; j++)
                    {
                        dotY[j] = resultlat[i];
                    }
                }
               

                for (int i = 0; i < latLen * lonLen; i++)
                {
                    if ((dotX[i] >= wlon && dotX[i] <= elon) && (dotY[i] >= slat && dotY[i] <= nlat))
                    {
                        newdotX[index] = dotX[i];
                        newdotY[index] = dotY[i];
                        resultCurrentu[index] = fileCurrentu[i];
                        resultCurrentv[index] = fileCurrentv[i];
                        index++;
                    }
                }

            }
            
        }
        private void otherfeaturesR(string featureType,int lonLen,int latLen,int ncid,int varid,double wlon,double elon,double slat,double nlat) {
            index = 0;
            float[] resultCHL = new float[lonLen * latLen];
            double[] resultnotCHL = new double[lonLen * latLen];
            int[] origin = new int[] { 0, 0 };
            int[] size = new int[] { lonLen, latLen };
            dotX = new double[latLen * lonLen];
            dotY = new double[latLen * lonLen];

            newdotX = new double[latLen * lonLen];
            newdotY = new double[latLen * lonLen];
            resultOtherFeatures = new float[latLen * lonLen];
            if (featureType == "chl")
            {
               
                NetCDF.nc_get_var_float(ncid, varid,  resultCHL);//叶绿素浓度有点问题读不出来
                //for (int i = 0; i < latLen; i++)//此循环将51*51格网内起始坐标按行进行组织成一维数组便于进一步的计算
                //{
                //    resultlon.CopyTo(dotX, resultlon.Length * i);
                //    for (int j = i * lonLen; j < (i + 1) * lonLen; j++)
                //    {
                //        dotY[j] = resultlat[i];
                //    }
                //}
                for (int i = 0; i < lonLen; i++)//此循环将51*51格网内起始坐标按行进行组织成一维数组便于进一步的计算
                {
                    resultlat.CopyTo(dotY, resultlat.Length * i);
                    for (int j = i * latLen; j < (i + 1) * latLen; j++)
                    {
                        dotX[j] = resultlon[i];
                    }
                }
                for (int i = 0; i < lonLen * latLen; i++)
                {
                    if ((dotX[i] >= wlon && dotX[i] <= elon) && (dotY[i] >= slat && dotY[i] <= nlat))
                    {
                        newdotX[index] = dotX[i];
                        newdotY[index] = dotY[i];
                        resultOtherFeatures[index] = resultCHL[i];

                        index++;
                    }
                }
            }
            else {
                
               NetCDF.nc_get_var_double(ncid, varid, resultnotCHL);
                for (int i = 0; i < latLen; i++)//此循环将51*51格网内起始坐标按行进行组织成一维数组便于进一步的计算
                {
                    resultlon.CopyTo(dotX, resultlon.Length * i);
                    for (int j = i * lonLen; j < (i + 1) * lonLen; j++)
                    {
                        dotY[j] = resultlat[i];
                    }
                }

                for (int i = 0; i < lonLen * latLen; i++)
                {
                    if ((dotX[i] >= wlon && dotX[i] <= elon) && (dotY[i] >= slat && dotY[i] <= nlat))
                    {
                        newdotX[index] = dotX[i];
                        newdotY[index] = dotY[i];
                        resultOtherFeatures[index] = (float) resultnotCHL[i];

                        index++;
                    }
                }
            }
              
           
        }
    }
}
