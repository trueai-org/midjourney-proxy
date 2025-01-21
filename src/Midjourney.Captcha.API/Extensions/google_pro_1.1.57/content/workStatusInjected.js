/******/ (() => { // webpackBootstrap
/******/ 	"use strict";
/******/ 	// The require scope
/******/ 	var __webpack_require__ = {};
/******/ 	
/************************************************************************/
/******/ 	/* webpack/runtime/make namespace object */
/******/ 	(() => {
/******/ 		// define __esModule on exports
/******/ 		__webpack_require__.r = (exports) => {
/******/ 			if(typeof Symbol !== 'undefined' && Symbol.toStringTag) {
/******/ 				Object.defineProperty(exports, Symbol.toStringTag, { value: 'Module' });
/******/ 			}
/******/ 			Object.defineProperty(exports, '__esModule', { value: true });
/******/ 		};
/******/ 	})();
/******/ 	
/************************************************************************/
var __webpack_exports__ = {};
/*!*********************************************!*\
  !*** ./src/function/injected/workStatus.ts ***!
  \*********************************************/
__webpack_require__.r(__webpack_exports__);
var workStatusFlagManager = /** @class */ (function () {
    function workStatusFlagManager() {
        var _this = this;
        console.log('fuck!!!!!!!!!!!!!!!!');
        window.addEventListener("message", function (_a) {
            var data = _a.data;
            var crx = data.crx, type = data.type;
            if (crx !== 'yesCaptcha')
                return;
            if (type !== "workStatusChange" && (data === null || data === void 0 ? void 0 : data.result)) {
                _this.updateStatus(data.result);
            }
            if (type !== "initWorkStatus" && (data === null || data === void 0 ? void 0 : data.result)) {
                _this.initConfig(data === null || data === void 0 ? void 0 : data.result);
            }
        });
    }
    workStatusFlagManager.prototype.initConfig = function (flagName) {
        this.flagName = flagName;
    };
    workStatusFlagManager.prototype.updateStatus = function (statusStr) {
        if (!this.flagName)
            return;
        window[this.flagName] = statusStr;
    };
    return workStatusFlagManager;
}());
new workStatusFlagManager();


/******/ })()
;
//# sourceMappingURL=data:application/json;charset=utf-8;base64,eyJ2ZXJzaW9uIjozLCJmaWxlIjoiY29udGVudC93b3JrU3RhdHVzSW5qZWN0ZWQuanMiLCJtYXBwaW5ncyI6Ijs7VUFBQTtVQUNBOzs7OztXQ0RBO1dBQ0E7V0FDQTtXQUNBLHVEQUF1RCxpQkFBaUI7V0FDeEU7V0FDQSxnREFBZ0QsYUFBYTtXQUM3RDs7Ozs7Ozs7O0FDSkE7SUFDSTtRQUFBLGlCQVlDO1FBWEcsT0FBTyxDQUFDLEdBQUcsQ0FBQyxzQkFBc0IsQ0FBQztRQUNuQyxNQUFNLENBQUMsZ0JBQWdCLENBQUMsU0FBUyxFQUFFLFVBQUMsRUFBcUM7Z0JBQW5DLElBQUk7WUFDOUIsT0FBRyxHQUFXLElBQUksSUFBZixFQUFFLElBQUksR0FBSyxJQUFJLEtBQVQsQ0FBUztZQUMxQixJQUFJLEdBQUcsS0FBSyxZQUFZO2dCQUFFLE9BQU07WUFDaEMsSUFBSSxJQUFJLEtBQUssa0JBQWtCLEtBQUksSUFBSSxhQUFKLElBQUksdUJBQUosSUFBSSxDQUFFLE1BQU0sR0FBRTtnQkFDN0MsS0FBSSxDQUFDLFlBQVksQ0FBQyxJQUFJLENBQUMsTUFBZ0IsQ0FBQzthQUMzQztZQUNELElBQUksSUFBSSxLQUFLLGdCQUFnQixLQUFJLElBQUksYUFBSixJQUFJLHVCQUFKLElBQUksQ0FBRSxNQUFNLEdBQUU7Z0JBQzNDLEtBQUksQ0FBQyxVQUFVLENBQUMsSUFBSSxhQUFKLElBQUksdUJBQUosSUFBSSxDQUFFLE1BQWdCLENBQUM7YUFDMUM7UUFDTCxDQUFDLENBQUMsQ0FBQztJQUNQLENBQUM7SUFFRCwwQ0FBVSxHQUFWLFVBQVcsUUFBZ0I7UUFDdkIsSUFBSSxDQUFDLFFBQVEsR0FBRyxRQUFRO0lBQzVCLENBQUM7SUFDRCw0Q0FBWSxHQUFaLFVBQWEsU0FBaUI7UUFDMUIsSUFBSSxDQUFDLElBQUksQ0FBQyxRQUFRO1lBQUUsT0FBTTtRQUMxQixNQUFNLENBQUMsSUFBSSxDQUFDLFFBQVEsQ0FBQyxHQUFHLFNBQVM7SUFDckMsQ0FBQztJQUNMLDRCQUFDO0FBQUQsQ0FBQztBQUVELElBQUkscUJBQXFCLEVBQUUiLCJzb3VyY2VzIjpbIndlYnBhY2s6Ly9lemJ1eV9hc3Npc3RhbnQvd2VicGFjay9ib290c3RyYXAiLCJ3ZWJwYWNrOi8vZXpidXlfYXNzaXN0YW50L3dlYnBhY2svcnVudGltZS9tYWtlIG5hbWVzcGFjZSBvYmplY3QiLCJ3ZWJwYWNrOi8vZXpidXlfYXNzaXN0YW50Ly4vc3JjL2Z1bmN0aW9uL2luamVjdGVkL3dvcmtTdGF0dXMudHMiXSwic291cmNlc0NvbnRlbnQiOlsiLy8gVGhlIHJlcXVpcmUgc2NvcGVcbnZhciBfX3dlYnBhY2tfcmVxdWlyZV9fID0ge307XG5cbiIsIi8vIGRlZmluZSBfX2VzTW9kdWxlIG9uIGV4cG9ydHNcbl9fd2VicGFja19yZXF1aXJlX18uciA9IChleHBvcnRzKSA9PiB7XG5cdGlmKHR5cGVvZiBTeW1ib2wgIT09ICd1bmRlZmluZWQnICYmIFN5bWJvbC50b1N0cmluZ1RhZykge1xuXHRcdE9iamVjdC5kZWZpbmVQcm9wZXJ0eShleHBvcnRzLCBTeW1ib2wudG9TdHJpbmdUYWcsIHsgdmFsdWU6ICdNb2R1bGUnIH0pO1xuXHR9XG5cdE9iamVjdC5kZWZpbmVQcm9wZXJ0eShleHBvcnRzLCAnX19lc01vZHVsZScsIHsgdmFsdWU6IHRydWUgfSk7XG59OyIsImltcG9ydCB7IENvbmZpZ1R5cGUsIFllc0NhcHRjaGFNZXNzYWdlIH0gZnJvbSBcIi4uLy4uL3R5cGVzXCI7XHJcblxyXG5jbGFzcyB3b3JrU3RhdHVzRmxhZ01hbmFnZXIge1xyXG4gICAgY29uc3RydWN0b3IoKSB7XHJcbiAgICAgICAgY29uc29sZS5sb2coJ2Z1Y2shISEhISEhISEhISEhISEhJylcclxuICAgICAgICB3aW5kb3cuYWRkRXZlbnRMaXN0ZW5lcihcIm1lc3NhZ2VcIiwgKHsgZGF0YSB9OiB7IGRhdGE6IFllc0NhcHRjaGFNZXNzYWdlIH0pID0+IHtcclxuICAgICAgICAgICAgY29uc3QgeyBjcngsIHR5cGUgfSA9IGRhdGFcclxuICAgICAgICAgICAgaWYgKGNyeCAhPT0gJ3llc0NhcHRjaGEnKSByZXR1cm5cclxuICAgICAgICAgICAgaWYgKHR5cGUgIT09IFwid29ya1N0YXR1c0NoYW5nZVwiICYmIGRhdGE/LnJlc3VsdCkge1xyXG4gICAgICAgICAgICAgICAgdGhpcy51cGRhdGVTdGF0dXMoZGF0YS5yZXN1bHQgYXMgc3RyaW5nKVxyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGlmICh0eXBlICE9PSBcImluaXRXb3JrU3RhdHVzXCIgJiYgZGF0YT8ucmVzdWx0KSB7XHJcbiAgICAgICAgICAgICAgICB0aGlzLmluaXRDb25maWcoZGF0YT8ucmVzdWx0IGFzIHN0cmluZylcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0pO1xyXG4gICAgfVxyXG4gICAgZmxhZ05hbWU6IHN0cmluZyB8IHVuZGVmaW5lZFxyXG4gICAgaW5pdENvbmZpZyhmbGFnTmFtZTogc3RyaW5nKSB7XHJcbiAgICAgICAgdGhpcy5mbGFnTmFtZSA9IGZsYWdOYW1lXHJcbiAgICB9XHJcbiAgICB1cGRhdGVTdGF0dXMoc3RhdHVzU3RyOiBzdHJpbmcpIHtcclxuICAgICAgICBpZiAoIXRoaXMuZmxhZ05hbWUpIHJldHVyblxyXG4gICAgICAgIHdpbmRvd1t0aGlzLmZsYWdOYW1lXSA9IHN0YXR1c1N0clxyXG4gICAgfVxyXG59XHJcblxyXG5uZXcgd29ya1N0YXR1c0ZsYWdNYW5hZ2VyKCkiXSwibmFtZXMiOltdLCJzb3VyY2VSb290IjoiIn0=