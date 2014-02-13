using System;
using System.Collections.Generic;
using TradingMotion.SDK.Algorithms;
using TradingMotion.SDK.Algorithms.InputParameters;
using TradingMotion.SDK.Markets.Charts;
using TradingMotion.SDK.Markets.Orders;
using TradingMotion.SDK.Markets.Indicators.Momentum;

/// <summary>
/// Cersei rules:
///     * Entry: Buy when price breaks Rate-of-Change level
///     * Exit: Sets a Trailing Stop based on the entry price and moves according to price raise
///     * Filters: None
/// </summary>
namespace CerseiStrategy
{
    public class CerseiStrategy : Strategy
    {
        ROCR100Indicator rocr100Indicator;
        Order trailingStopOrder;

        decimal acceleration;
        decimal highestClose;

        public CerseiStrategy(Chart mainChart, List<Chart> secondaryCharts)
            : base(mainChart, secondaryCharts)
        {

        }

        /// <summary>
        /// Strategy Name
        /// </summary>
        /// <returns>The complete name of the strategy</returns>
        public override string Name
        {
            get
            {
                return "Cersei Strategy";
            }
        }

        /// <summary>
        /// Security filter that ensures the Position will be closed at the end of the trading session.
        /// </summary>
        public override bool ForceCloseIntradayPosition
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// Security filter that sets a maximum open position size of 1 contract (either side)
        /// </summary>
        public override uint MaxOpenPosition
        {
            get
            {
                return 1;
            }
        }

        /// <summary>
        /// This strategy uses the Advanced Order Management mode
        /// </summary>
        public override bool UsesAdvancedOrderManagement
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// Strategy Parameter definition
        /// </summary>
        public override InputParameterList SetInputParameters()
        {
            InputParameterList parameters = new InputParameterList();

            // The previous N bars period ROCR 100 indicator will use
            parameters.Add(new InputParameter("ROCR 100 Period", 48));

            // The distance between the entry and the initial stop loss order
            parameters.Add(new InputParameter("Trailing Stop Loss ticks distance", 85m));

            // Break level of ROCR 100 indicator we consider a buy signal
            parameters.Add(new InputParameter("ROCR 100 Buy signal trigger level", 103));

            return parameters;
        }

        /// <summary>
        /// Initialization method
        /// </summary>
        public override void OnInitialize()
        {
            log.Debug("CerseiStrategy onInitialize()");

            // Adding a ROCR 100 indicator to strategy
            // (see http://stockcharts.com/school/doku.php?id=chart_school:technical_indicators:price_oscillators)
            rocr100Indicator = new ROCR100Indicator(Bars.Close, (int)this.GetInputParameter("ROCR 100 Period"));
            this.AddIndicator("ROCR 100 indicator", rocr100Indicator);

            // Setting the initial acceleration for the trailing stop
            acceleration = 0.02m;

            // Setting the initial highest close
            highestClose = 0m;
        }

        /// <summary>
        /// Strategy enter/exit/filtering rules
        /// </summary>
        public override void OnNewBar()
        {
            decimal stopMargin = (decimal)this.GetInputParameter("Trailing Stop Loss ticks distance") * this.GetMainChart().Symbol.TickSize;

            int buySignal = (int)this.GetInputParameter("ROCR 100 Buy signal trigger level");

            if (rocr100Indicator.GetROCR100()[1] <= buySignal && rocr100Indicator.GetROCR100()[0] > buySignal && this.GetOpenPosition() == 0)
            {
                // BUY SIGNAL: Entering long and placing a trailing stop loss
                MarketOrder buyOrder = new MarketOrder(OrderSide.Buy, 1, "Enter long position");
                trailingStopOrder = new StopOrder(OrderSide.Sell, 1, this.Bars.Close[0] - stopMargin, "Trailing stop long exit");

                this.InsertOrder(buyOrder);
                this.InsertOrder(trailingStopOrder);

                // Resetting acceleration and highest close
                acceleration = 0.02m;
                highestClose = Bars.Close[0];
            }
            else if (this.GetOpenPosition() == 1)
            {
                // Checking if the price has moved in our favour
                if (this.Bars.Close[0] > highestClose)
                {
                    highestClose = this.Bars.Close[0];

                    // Increasing acceleration
                    acceleration = acceleration * (highestClose - trailingStopOrder.Price);

                    // Checking if trailing the stop order would exceed the current market price
                    if (trailingStopOrder.Price + acceleration < this.Bars.Close[0])
                    {
                        // Setting the new price for the trailing stop
                        trailingStopOrder.Price = trailingStopOrder.Price + acceleration;
                        this.ModifyOrder(trailingStopOrder);
                    }
                    else
                    {
                        // Cancelling the order and closing the position
                        MarketOrder exitLongOrder = new MarketOrder(OrderSide.Sell, 1, "Exit long position");

                        this.InsertOrder(exitLongOrder);
                        this.CancelOrder(trailingStopOrder);
                    }
                }
            }
        }
    }
}
