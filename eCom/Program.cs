using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace eCom
{
    /// <summary>
    /// Class : Chicken Farm
    /// Task : Uses Pricing Model to determine price drops
    /// Triggers events to call event handler in Reatiler
    /// Terminates after 10 Price drops.
    /// </summary>
    public class ChickenFarm
    {
        private PrcMdl prcmdl;
        private Int32 price;
        public delegate void priceSlashedEvent(Int32 np, Int32 op);
        public delegate void printTimesEvent();
        public delegate void printOrdersEvent();
        public static event priceSlashedEvent priceSlashed;
        public static event printTimesEvent printRetailTimes;
        public static event printOrdersEvent printOrders;
        private int[] numOfChicken;
        private int[] numOfPriceCuts;
        private DateTime curTime;
        private MultiCellBuffer buf;
        private int counter = 0;
        private int totalRetailers;
        private int priceCuts = 10;
        private EventWaitHandle[] wH;
        /// <summary>
        /// Parametrized Constructor 
        /// Sets total number of retailers
        /// numOf Chicken Available per retailer
        /// total price cuts
        /// </summary>
        /// <param name="numOfretailers"></param>
        /// <param name="MCB"></param>
        public ChickenFarm(int numOfretailers, MultiCellBuffer MCB)
        {
            this.buf = MCB;
            prcmdl = new PrcMdl();
            numOfChicken = new int[5];
            numOfPriceCuts = new int[5];
            curTime = DateTime.Now;
            totalRetailers = numOfretailers;
            for (int iter = 0; iter < 5; ++iter)
            {
                numOfChicken[iter] = 50;
                numOfPriceCuts[iter] = 0;
            }
            wH = new EventWaitHandle[1];
            wH[0] = new EventWaitHandle(false, EventResetMode.AutoReset);
        }
        /// <summary>
        /// Thread from Main 
        /// Print Order Time
        /// Print Order Details
        /// </summary>
        public void farmerFunc()
        {
            MultiCellBuffer.notifyOrder += new MultiCellBuffer.notifyOrderingEvent(this.notifyOrder);
            int p = prcmdl.getPrice();
            for (int iter = 0; iter < priceCuts; iter++)
            {
                Int32 np = prcmdl.cutPrice(p);
                price = p;

                Console.WriteLine("Old Price = {0}", p);
                Console.WriteLine("New Price is {0}", np);
                if (np < price)
                {
                    if (priceSlashed != null)
                    {
                        priceSlashed(np, price);
                    }
                }
                price = np;
                Thread.Sleep(500);
                p = prcmdl.changePrice();
            }

            while (counter < totalRetailers * priceCuts)
               Thread.Sleep(500);

            printOrders();
            printRetailTimes();
        }
        /// <summary>
        /// Event handler for Multicell Buffer Event to catch new order placed
        /// </summary>
        /// <param name="cellsOccupied"></param>
        public void notifyOrder(bool cellsOccupied)
        {
            String[] orderString = new String[1];
            orderString[0] = buf.getOneCell();
            MultiCellBuffer.cells.Release();
            Decoder dec = new Decoder(orderString[0]);

            prcmdl.scalePrice(Convert.ToInt32(dec.getDecodedObject().getPrice()));
            this.setOrder(orderString);
            
            Console.WriteLine("Order from Sender Id {0} -- Received By Retailer", dec.getDecodedObject().getsenderId().ToString());

            //Auto Resetting Events
            wH[0].Set();
        }
        /// <summary>
        /// Send order to decoder to cover String to Order Object
        /// </summary>
        /// <param name="orderString"></param>
        public void setOrder(String[] orderString)
        {
            Decoder dec = new Decoder();
            OrderObject order;
            Thread[] thread = new Thread[orderString.Length];
            for (int i = 0; i < orderString.Length; ++i)
            {
                dec.DecodeObject(orderString[i]);
                order = dec.getDecodedObject();

                thread[i] = new Thread(new ParameterizedThreadStart(Worker));
                thread[i].Start(order);

                counter++;

            }

        }
        private static void Worker(object order)
        {
            OrdProcessing p = new OrdProcessing((OrderObject)order);
        }
    }
    /// <summary>
    /// Class : Pricing Model
    /// Task :Fluctuates prices
    /// </summary>
    public class PrcMdl
    {
        private int[] priceTable;
        private int dayOfWeek;
        public PrcMdl()
        {
            priceTable = new int[7];
            for (int i = 0; i < priceTable.Length; ++i)
            {
                priceTable[i] = (i + (i / 2) - (i * 2)) + 100;
                if (i > 4)
                {
                    priceTable[i] += 20;
                }

            }
            dayOfWeek = 0;
        }
        /// <summary>
        /// Change Price function to fluctuate prices
        /// </summary>
        /// <returns></returns>
        public int changePrice()
        {
            int price = priceTable[dayOfWeek];
            dayOfWeek = (dayOfWeek + 1) % priceTable.Length;
            return price;
        }
        /// <summary>
        /// Increase price by 100%
        /// </summary>
        /// <param name="num"></param>
        /// <returns></returns>
        public int scalePrice(int incPrice)
        {
            return incPrice * 2;
        }
        /// <summary>
        /// Reduce price by 10%
        /// </summary>
        /// <param name="num"></param>
        /// <returns></returns>
        public int cutPrice(int decPrice)
        {
            return decPrice * 90 / 100;
        }
        /// <summary>
        /// Retrieve Prices
        /// </summary>
        /// <returns></returns>
        public int getPrice()
        {
            return priceTable[dayOfWeek];
        }
    }
    /// <summary>
    /// Class : Retailer
    /// Task : evaluates the price
    /// generates an OrderObject (consisting of multiple values), 
    /// and sends the order to the Encoder to convert the order 
    /// object into a plain string.
    /// </summary>
    public class Retailer
    {
        private int senderId;
        private int startTime;
        private int chickensToOrder;
        private double tempChickenOrder;
        private Random rng;
        private DateTime[] orderStart;
        private DateTime[] orderFinish;
        private List<OrderObject> queuedOrd;
        private List<OrderObject> sentOrd;
        private List<OrderObject> cnfOrd;
        private OrderObject curOrd;
        private Encoder objEnc;
        private MultiCellBuffer mulCelBuf;
        private int numOfOrders;
        /// <summary>
        /// Constructor to initialize objects of the class 
        /// </summary>
        public Retailer()
        {
            rng = new Random();
            queuedOrd = new List<OrderObject>();
            sentOrd = new List<OrderObject>();
            cnfOrd = new List<OrderObject>();
            curOrd = new OrderObject();
            objEnc = new Encoder();
            orderStart = new DateTime[10];
            orderFinish = new DateTime[10];
            startTime = 0;
            numOfOrders = 0;
            mulCelBuf = new MultiCellBuffer();
        }
        /// <summary>
        /// Thread that is run from Mains for starting retailer functions
        /// </summary>
        public void retailerFunc()
        {
            Console.WriteLine("Sender is: {0}", senderId);
            //To lock function to achieve correct values by locking
            bool bLock = true;
            while (bLock)
            {
                Thread.Sleep(500);
                if (queuedOrd.Count != 0 && startTime < 10)
                {
                    objEnc.EncodeOrder(queuedOrd.First());
                    Console.WriteLine("Sending Order from Sender {0} ", queuedOrd.First().getsenderId().ToString());
                    sentOrd.Add(queuedOrd.First());
                    queuedOrd.RemoveAt(0);
                   
                    MultiCellBuffer.cells.WaitOne();
                    orderStart[startTime] = DateTime.Now;
                    mulCelBuf.setOneCell(objEnc.getEncodedOrder());

                    startTime++;

                }
                else if (startTime >= 10)
                    bLock = false;

            }
        }
        /// <summary>
        /// Function used later in the same class to hold sender Id
        /// </summary>
        /// <param name="id"></param>
        public void setId(int id)
        {
            senderId = id;
            rng = new Random(id * System.DateTime.Now.GetHashCode());
        }
        /// <summary>
        /// Event Handler for catching prices dropped events
        /// </summary>
        /// <param name="np"></param>
        /// <param name="op"></param>
        public void priceSlashedEvent(int np, int op)
        {
            if(numOfOrders < 10)
            {
                chickensToOrder = rng.Next(10, 20);
                tempChickenOrder = ((double)op / (double)np) * chickensToOrder;
                chickensToOrder = (int)tempChickenOrder;

                curOrd.setamount(chickensToOrder);
                curOrd.setsenderId(senderId.ToString());
                curOrd.setcardNum(rng.Next(5000, 7000));
                curOrd.setPrice(np);
                numOfOrders++;
                queuedOrd.Add(curOrd);

                curOrd = new OrderObject();
            }
        }
        /// <summary>
        /// Event Handler to catch event from Order procesing after a successful event Confirmation
        /// </summary>
        /// <param name="Obj"></param>
        public void ordConfirmEvt(OrderObject Obj)
        {
            if (Obj.getsenderId() == senderId.ToString())
            {
                for (int s = 0; s < sentOrd.Count; s++)
                {
                    if (sentOrd.ElementAt(s).getcardNum() == Obj.getcardNum())
                    {
                        Console.WriteLine("Order {0} Confirmation Received", Obj.getsenderId().ToString());
                        orderFinish[s] = DateTime.Now;
                        cnfOrd.Add(Obj);
                    }
                }
            }
        }
        /// <summary>
        /// Event to print Order Time
        /// </summary>
        public void printTimes()
        {
            if (cnfOrd.Count > 0)
            {
                for (int i = 0; i < cnfOrd.Count -1; i++)
                {
                    Console.WriteLine("Order {0}:       Start Time: {1} Seconds   Count : {2}", cnfOrd[i].getsenderId(), orderStart[i].ToString(), cnfOrd.Count);
                }
                long elapsedTime = orderFinish[cnfOrd.Count - 1].Ticks - orderStart[0].Ticks;
                double elapsedSec = TimeSpan.FromTicks(elapsedTime).TotalSeconds;
                Console.WriteLine("Time Elapsed between start and processing: {0}", elapsedSec);
                Console.WriteLine("Turn Around Time: {0}", elapsedSec / cnfOrd.Count);
            }

        }
        /// <summary>
        /// Event to print order details e.g. In Queue, Sent or Confirmed Orders
        /// </summary>
        public void printOrders()
        {
            if (queuedOrd.Count > 0)
            {
                for (int iter = 0; iter < queuedOrd.Count; iter++)
                {
                    Console.WriteLine("In Queue Order {0}", queuedOrd[iter].getsenderId().ToString());
                }
            }

            if (sentOrd.Count > 0)
            {
                for (int iter = 0; iter < sentOrd.Count; iter++)
                {
                    Console.WriteLine("Sent Order {0}", sentOrd[iter].getsenderId().ToString());
                }
            }
            if (cnfOrd.Count > 0)
            {
                for (int iter = 0; iter < cnfOrd.Count; iter++)
                {
                    Console.WriteLine("Confirmed Order {0}", cnfOrd[iter].getsenderId().ToString());
                }
            }
        }
    }
    /// <summary>
    /// Order Object Class 
    /// Set Order Details for each order
    /// </summary>
    public class OrderObject
    {
        private String senderId;
        private int cardNum;
        private int amount;
        private double price;
        private double total;
        /// <summary>
        /// Method to get Sender Id
        /// </summary>
        /// <returns></returns>
        public String getsenderId()
        {
            return senderId;
        }
        /// <summary>
        /// Method to set Sender Id
        /// </summary>
        /// <returns></returns>
        public void setsenderId(String sID)
        {
            senderId = sID;
        }
        /// <summary>
        /// Method to get Card Number
        /// </summary>
        /// <returns></returns>
        public int getcardNum()
        {
            return cardNum;
        }
        /// <summary>
        /// Method to set Card Number
        /// </summary>
        /// <returns></returns>
        public void setcardNum(int cNum)
        {
            cardNum = cNum;
        }
        /// <summary>
        /// Method to get number of chickens
        /// </summary>
        /// <returns></returns>
        public int getamount()
        {
            return amount;
        }
        /// <summary>
        /// Method to set Number of Chickens
        /// </summary>
        /// <returns></returns>
        public void setamount(int amt)
        {
            amount = amt;
        }
        /// <summary>
        /// Method to set Price of Chicken
        /// </summary>
        /// <returns></returns>
        public void setPrice(double p)
        {
            price = p;
        }
        /// <summary>
        /// Method to get Price of Chicken
        /// </summary>
        /// <returns></returns>
        public double getPrice()
        {
            return price;
        }
        /// <summary>
        /// Method to set Total Price of Order
        /// </summary>
        /// <returns></returns>
        public void setTotal(double t)
        {
            total = t;
        }
        /// <summary>
        /// Method to get Total Price of Order
        /// </summary>
        /// <returns></returns>
        public double getTotal()
        {
            return total;
        }

    }
    /// <summary>
    /// Class : Encoder
    /// Task : Encodes Order Object Used by Retailer
    /// </summary>
    public class Encoder
    {
        private String EncodedOrder;
        /// <summary>
        /// Method to Encode Order Object Passed into string seperated by "-"
        /// </summary>
        /// <param name="O"></param>
        public void EncodeOrder(OrderObject O)
        {
            Console.WriteLine("Order Encoding in Process");
            StringBuilder OrdString = new StringBuilder();
            OrdString.Append(O.getsenderId().ToString());
            OrdString.Append("-");
            OrdString.Append(O.getcardNum().ToString());
            OrdString.Append("-");
            OrdString.Append(O.getamount().ToString());
            OrdString.Append("-");
            OrdString.Append(O.getPrice().ToString());
            OrdString.Append("-");
            OrdString.Append(O.getTotal().ToString());
            EncodedOrder = OrdString.ToString();
        }
        /// <summary>
        /// //Method to get Encoded Order Object as string seperated by "-"
        /// </summary>
        /// <returns></returns>
        public String getEncodedOrder()
        {
            return EncodedOrder;
        }
    }
    /// <summary>
    /// Class : Decoder
    /// Task : Decodes Order String into Order Object Used by Chiken Farm
    /// </summary>
    public class Decoder
    {
        private OrderObject DecodedOrder;
        /// <summary>
        /// Non Parametrized Constructor
        /// </summary>
        public Decoder()
        {
            DecodedOrder = new OrderObject();
        }
        /// <summary>
        /// Parametrized Constructor Passed String as argument
        /// </summary>
        /// <param name="S"></param>
        public Decoder(String S)
        {
            DecodedOrder = new OrderObject();
            DecodeObject(S);
        }
        /// <summary>
        /// Method to get Decoded Object
        /// </summary>
        /// <returns></returns>
        public OrderObject getDecodedObject()
        {
            return DecodedOrder;
        }
        /// <summary>
        /// Method to Decode Object by splitting "-"
        /// </summary>
        /// <param name="O"></param>
        public void DecodeObject(String O)
        {
            Console.WriteLine("Order Decoding In Process");
            String[] OrdObjArr = O.Split('-');
            String sId = OrdObjArr[0].ToString();
            DecodedOrder.setsenderId(sId);
            int cardNum = Convert.ToInt32(OrdObjArr[1].ToString());
            DecodedOrder.setcardNum(cardNum);
            int amt = Convert.ToInt32(OrdObjArr[2].ToString());
            DecodedOrder.setamount(amt);
            double prc = Convert.ToDouble(OrdObjArr[3].ToString());
            DecodedOrder.setPrice(prc);
            double tot = Convert.ToDouble(OrdObjArr[4].ToString());
            DecodedOrder.setPrice(tot);
        }

    }
    /// <summary>
    /// Class : Order Processing
    /// Task : Checks Card Number is Valid or Not
    /// Confirms and Triggers Order Confirmation Event
    /// </summary>
    public class OrdProcessing
    {
        private OrderObject Ord;
        public delegate void ordConfirmEvt(OrderObject cnfOrd);
        public static event ordConfirmEvt ordCnf;
        /// <summary>
        /// Method to send notification to Retailer Class as the order is confirmed based on the card number checking
        /// And Sender Id confirmation
        /// </summary>
        /// <param name="O"></param>
        public OrdProcessing(OrderObject O)
        {
            this.Ord = O;
            if (Ord.getcardNum() > 5050 && Ord.getcardNum() < 7000)
            {
                double total=0.0, tax =0.0, shippingAndHandling =0.0;
                tax = 0.14 * Ord.getPrice() * Ord.getamount();
                shippingAndHandling = 0.01 * Ord.getPrice(); 
                total = Ord.getamount() + tax + shippingAndHandling;
                Ord.setTotal(total);

                if(ordCnf != null)
                {
                    ordCnf(Ord);
                }

            }
            else
                Console.WriteLine("Invalid Card {0}", Ord.getcardNum());
        }
        /// <summary>
        /// Method to retrieve back the Order
        /// </summary>
        /// <returns></returns>
        public OrderObject getOrd()
        {
            return Ord;
        }
    }
    /// <summary>
    /// Class : MultiCell Buffer
    /// Task : Send Orders from Retailer to Chicken Farm 
    /// </summary>
    public class MultiCellBuffer
    {
        static int numOfCell = 2;
        public static Semaphore cells = new Semaphore(numOfCell, numOfCell);
        public delegate void notifyOrderingEvent(bool occupied);
        public static event notifyOrderingEvent notifyOrder;
        static string[] ncells = new string[numOfCell];
        static int counter = 0;
        /// <summary>
        /// Method to put item into buffer
        /// </summary>
        /// <param name="order"></param>
        public void setOneCell(String order)
        {
            bool bLock = false;

            for (int i = 0; i < ncells.Length && !bLock; ++i)
            {
                if (ncells[i] == null)
                {
                    ncells[i] = order;
                    bLock = true;
                }
            }
            if (!bLock)
            {
                Console.WriteLine("Cells are full, Could not write to buffer");
            }
            else
            {
                notifyOrder(true);
            }
        }
        /// <summary>
        /// Get Item from the buffer
        /// </summary>
        /// <returns></returns>
        public string getOneCell()
        {
            string order = null;
            if (!cellsEmpty())
            {
                while (ncells[counter] == null)
                {
                    counter = (counter + 1) % ncells.Length;
                }
                order = ncells[counter];
                ncells[counter] = null;
                counter = (counter + 1) % ncells.Length;

            }
            return order;
        }
        /// <summary>
        /// Check if the cells in buffer are empty
        /// </summary>
        /// <returns></returns>
        public bool cellsEmpty()
        {
            bool empty = true;
            for (int i = 0; i < ncells.Length; ++i)
            {
                if (ncells[i] != null)
                    empty = false;
            }

            if (empty)
                notifyOrder(false);

            return empty;
        }

    }
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=====Main Executing========");
            MultiCellBuffer buffer = new MultiCellBuffer();
            
            ChickenFarm ck = new ChickenFarm(5, buffer);

            Thread CkFarmThread = new Thread(new ThreadStart(ck.farmerFunc));
            //Start Chicken Farm thread
            CkFarmThread.Start();
            // Number of threads
            int nthreads = 10;
            const int idNum = 100;

            Retailer[] ret = new Retailer[nthreads];
            Thread[] RetThread = new Thread[nthreads];

            for (int i = 0; i < nthreads; i++)
            {
                ret[i] = new Retailer();
                ret[i].setId(idNum + i);
                ChickenFarm.priceSlashed += new ChickenFarm.priceSlashedEvent(ret[i].priceSlashedEvent);
                ChickenFarm.printRetailTimes += new ChickenFarm.printTimesEvent(ret[i].printTimes);
                ChickenFarm.printOrders += new ChickenFarm.printOrdersEvent(ret[i].printOrders);
                RetThread[i] = new Thread(new ThreadStart(ret[i].retailerFunc));
                OrdProcessing.ordCnf += new OrdProcessing.ordConfirmEvt(ret[i].ordConfirmEvt);
                RetThread[i].Name = (i + 1).ToString();
                // STart Retailer Thread
                RetThread[i].Start();
            }
        }
    }
}
