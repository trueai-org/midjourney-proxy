/******/ (() => { // webpackBootstrap
/******/ 	"use strict";
/******/ 	var __webpack_modules__ = ({

/***/ "./src/function/injector.ts":
/*!**********************************!*\
  !*** ./src/function/injector.ts ***!
  \**********************************/
/***/ ((__unused_webpack_module, __webpack_exports__, __webpack_require__) => {

__webpack_require__.r(__webpack_exports__);
/* harmony export */ __webpack_require__.d(__webpack_exports__, {
/* harmony export */   "injectCustomJs": () => (/* binding */ injectCustomJs),
/* harmony export */   "ready": () => (/* binding */ ready)
/* harmony export */ });
function injectCustomJs(jsPath) {
    jsPath = jsPath;
    var temp = document.createElement('script');
    temp.setAttribute('type', 'text/javascript');
    // 获得的地址类似：chrome-extension://ihcokhadfjfchaeagdoclpnjdiokfakg/js/inject.js
    temp.src = chrome.runtime.getURL(jsPath);
    // temp.onload = function () {
    //     // 放在页面不好看，执行完后移除掉
    //     this.parentNode.removeChild(this);
    // };
    document.head.appendChild(temp);
}
function ready(fn) {
    var timer = setInterval(function () {
        if (document === null || document === void 0 ? void 0 : document.head) {
            console.log('成功注入！！！！');
            fn();
            clearInterval(timer);
        }
    }, 1);
}


/***/ })

/******/ 	});
/************************************************************************/
/******/ 	// The module cache
/******/ 	var __webpack_module_cache__ = {};
/******/ 	
/******/ 	// The require function
/******/ 	function __webpack_require__(moduleId) {
/******/ 		// Check if module is in cache
/******/ 		var cachedModule = __webpack_module_cache__[moduleId];
/******/ 		if (cachedModule !== undefined) {
/******/ 			return cachedModule.exports;
/******/ 		}
/******/ 		// Create a new module (and put it into the cache)
/******/ 		var module = __webpack_module_cache__[moduleId] = {
/******/ 			// no module.id needed
/******/ 			// no module.loaded needed
/******/ 			exports: {}
/******/ 		};
/******/ 	
/******/ 		// Execute the module function
/******/ 		__webpack_modules__[moduleId](module, module.exports, __webpack_require__);
/******/ 	
/******/ 		// Return the exports of the module
/******/ 		return module.exports;
/******/ 	}
/******/ 	
/************************************************************************/
/******/ 	/* webpack/runtime/define property getters */
/******/ 	(() => {
/******/ 		// define getter functions for harmony exports
/******/ 		__webpack_require__.d = (exports, definition) => {
/******/ 			for(var key in definition) {
/******/ 				if(__webpack_require__.o(definition, key) && !__webpack_require__.o(exports, key)) {
/******/ 					Object.defineProperty(exports, key, { enumerable: true, get: definition[key] });
/******/ 				}
/******/ 			}
/******/ 		};
/******/ 	})();
/******/ 	
/******/ 	/* webpack/runtime/hasOwnProperty shorthand */
/******/ 	(() => {
/******/ 		__webpack_require__.o = (obj, prop) => (Object.prototype.hasOwnProperty.call(obj, prop))
/******/ 	})();
/******/ 	
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
// This entry need to be wrapped in an IIFE because it need to be isolated against other modules in the chunk.
(() => {
/*!*****************************!*\
  !*** ./src/content/test.ts ***!
  \*****************************/
__webpack_require__.r(__webpack_exports__);
/* harmony import */ var _function_injector__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! ../function/injector */ "./src/function/injector.ts");

var isSudokuPage = /hcaptcha\S*\.com(.*?)frame=challenge/.test(location.href);
var isCheckboxPage = /hcaptcha\S*\.com(.*?)frame=checkbox/.test(location.href);
if (isSudokuPage || isCheckboxPage) {
    console.log('fuck0');
    (0,_function_injector__WEBPACK_IMPORTED_MODULE_0__.ready)(function () { return (0,_function_injector__WEBPACK_IMPORTED_MODULE_0__.injectCustomJs)("content/injected.js"); });
}

})();

/******/ })()
;
//# sourceMappingURL=data:application/json;charset=utf-8;base64,eyJ2ZXJzaW9uIjozLCJmaWxlIjoiY29udGVudC90ZXN0LmpzIiwibWFwcGluZ3MiOiI7Ozs7Ozs7Ozs7Ozs7OztBQUVPLFNBQVMsY0FBYyxDQUFDLE1BQWM7SUFDekMsTUFBTSxHQUFHLE1BQU0sQ0FBQztJQUNoQixJQUFNLElBQUksR0FBRyxRQUFRLENBQUMsYUFBYSxDQUFDLFFBQVEsQ0FBQyxDQUFDO0lBQzlDLElBQUksQ0FBQyxZQUFZLENBQUMsTUFBTSxFQUFFLGlCQUFpQixDQUFDLENBQUM7SUFDN0MsMkVBQTJFO0lBQzNFLElBQUksQ0FBQyxHQUFHLEdBQUcsTUFBTSxDQUFDLE9BQU8sQ0FBQyxNQUFNLENBQUMsTUFBTSxDQUFDLENBQUM7SUFDekMsOEJBQThCO0lBQzlCLHlCQUF5QjtJQUN6Qix5Q0FBeUM7SUFDekMsS0FBSztJQUNMLFFBQVEsQ0FBQyxJQUFJLENBQUMsV0FBVyxDQUFDLElBQUksQ0FBQyxDQUFDO0FBQ3BDLENBQUM7QUFFTSxTQUFTLEtBQUssQ0FBQyxFQUFPO0lBQ3pCLElBQU0sS0FBSyxHQUFHLFdBQVcsQ0FBQztRQUN0QixJQUFJLFFBQVEsYUFBUixRQUFRLHVCQUFSLFFBQVEsQ0FBRSxJQUFJLEVBQUU7WUFDaEIsT0FBTyxDQUFDLEdBQUcsQ0FBQyxVQUFVLENBQUM7WUFDdkIsRUFBRSxFQUFFO1lBQ0osYUFBYSxDQUFDLEtBQUssQ0FBQztTQUN2QjtJQUNMLENBQUMsRUFBRSxDQUFDLENBQUM7QUFDVCxDQUFDOzs7Ozs7O1VDdkJEO1VBQ0E7O1VBRUE7VUFDQTtVQUNBO1VBQ0E7VUFDQTtVQUNBO1VBQ0E7VUFDQTtVQUNBO1VBQ0E7VUFDQTtVQUNBO1VBQ0E7O1VBRUE7VUFDQTs7VUFFQTtVQUNBO1VBQ0E7Ozs7O1dDdEJBO1dBQ0E7V0FDQTtXQUNBO1dBQ0EseUNBQXlDLHdDQUF3QztXQUNqRjtXQUNBO1dBQ0E7Ozs7O1dDUEE7Ozs7O1dDQUE7V0FDQTtXQUNBO1dBQ0EsdURBQXVELGlCQUFpQjtXQUN4RTtXQUNBLGdEQUFnRCxhQUFhO1dBQzdEOzs7Ozs7Ozs7Ozs7QUNONkQ7QUFFN0QsSUFBTSxZQUFZLEdBQUcsc0NBQXNDLENBQUMsSUFBSSxDQUFDLFFBQVEsQ0FBQyxJQUFJLENBQUM7QUFDL0UsSUFBTSxjQUFjLEdBQUcscUNBQXFDLENBQUMsSUFBSSxDQUFDLFFBQVEsQ0FBQyxJQUFJLENBQUM7QUFDaEYsSUFBSSxZQUFZLElBQUksY0FBYyxFQUFFO0lBQ2hDLE9BQU8sQ0FBQyxHQUFHLENBQUMsT0FBTyxDQUFDO0lBQ3BCLHlEQUFLLENBQUMsY0FBTSx5RUFBYyxDQUFDLHFCQUFxQixDQUFDLEVBQXJDLENBQXFDLENBQUM7Q0FDckQiLCJzb3VyY2VzIjpbIndlYnBhY2s6Ly9lemJ1eV9hc3Npc3RhbnQvLi9zcmMvZnVuY3Rpb24vaW5qZWN0b3IudHMiLCJ3ZWJwYWNrOi8vZXpidXlfYXNzaXN0YW50L3dlYnBhY2svYm9vdHN0cmFwIiwid2VicGFjazovL2V6YnV5X2Fzc2lzdGFudC93ZWJwYWNrL3J1bnRpbWUvZGVmaW5lIHByb3BlcnR5IGdldHRlcnMiLCJ3ZWJwYWNrOi8vZXpidXlfYXNzaXN0YW50L3dlYnBhY2svcnVudGltZS9oYXNPd25Qcm9wZXJ0eSBzaG9ydGhhbmQiLCJ3ZWJwYWNrOi8vZXpidXlfYXNzaXN0YW50L3dlYnBhY2svcnVudGltZS9tYWtlIG5hbWVzcGFjZSBvYmplY3QiLCJ3ZWJwYWNrOi8vZXpidXlfYXNzaXN0YW50Ly4vc3JjL2NvbnRlbnQvdGVzdC50cyJdLCJzb3VyY2VzQ29udGVudCI6WyJpbXBvcnQgeyB3YWl0Rm9yIH0gZnJvbSAnc3JjL2NvbW1vbic7XHJcblxyXG5leHBvcnQgZnVuY3Rpb24gaW5qZWN0Q3VzdG9tSnMoanNQYXRoOiBzdHJpbmcpIHtcclxuICAgIGpzUGF0aCA9IGpzUGF0aDtcclxuICAgIGNvbnN0IHRlbXAgPSBkb2N1bWVudC5jcmVhdGVFbGVtZW50KCdzY3JpcHQnKTtcclxuICAgIHRlbXAuc2V0QXR0cmlidXRlKCd0eXBlJywgJ3RleHQvamF2YXNjcmlwdCcpO1xyXG4gICAgLy8g6I635b6X55qE5Zyw5Z2A57G75Ly877yaY2hyb21lLWV4dGVuc2lvbjovL2loY29raGFkZmpmY2hhZWFnZG9jbHBuamRpb2tmYWtnL2pzL2luamVjdC5qc1xyXG4gICAgdGVtcC5zcmMgPSBjaHJvbWUucnVudGltZS5nZXRVUkwoanNQYXRoKTtcclxuICAgIC8vIHRlbXAub25sb2FkID0gZnVuY3Rpb24gKCkge1xyXG4gICAgLy8gICAgIC8vIOaUvuWcqOmhtemdouS4jeWlveeci++8jOaJp+ihjOWujOWQjuenu+mZpOaOiVxyXG4gICAgLy8gICAgIHRoaXMucGFyZW50Tm9kZS5yZW1vdmVDaGlsZCh0aGlzKTtcclxuICAgIC8vIH07XHJcbiAgICBkb2N1bWVudC5oZWFkLmFwcGVuZENoaWxkKHRlbXApO1xyXG59XHJcblxyXG5leHBvcnQgZnVuY3Rpb24gcmVhZHkoZm46IGFueSkge1xyXG4gICAgY29uc3QgdGltZXIgPSBzZXRJbnRlcnZhbCgoKSA9PiB7XHJcbiAgICAgICAgaWYgKGRvY3VtZW50Py5oZWFkKSB7XHJcbiAgICAgICAgICAgIGNvbnNvbGUubG9nKCfmiJDlip/ms6jlhaXvvIHvvIHvvIHvvIEnKVxyXG4gICAgICAgICAgICBmbigpXHJcbiAgICAgICAgICAgIGNsZWFySW50ZXJ2YWwodGltZXIpXHJcbiAgICAgICAgfVxyXG4gICAgfSwgMSlcclxufSIsIi8vIFRoZSBtb2R1bGUgY2FjaGVcbnZhciBfX3dlYnBhY2tfbW9kdWxlX2NhY2hlX18gPSB7fTtcblxuLy8gVGhlIHJlcXVpcmUgZnVuY3Rpb25cbmZ1bmN0aW9uIF9fd2VicGFja19yZXF1aXJlX18obW9kdWxlSWQpIHtcblx0Ly8gQ2hlY2sgaWYgbW9kdWxlIGlzIGluIGNhY2hlXG5cdHZhciBjYWNoZWRNb2R1bGUgPSBfX3dlYnBhY2tfbW9kdWxlX2NhY2hlX19bbW9kdWxlSWRdO1xuXHRpZiAoY2FjaGVkTW9kdWxlICE9PSB1bmRlZmluZWQpIHtcblx0XHRyZXR1cm4gY2FjaGVkTW9kdWxlLmV4cG9ydHM7XG5cdH1cblx0Ly8gQ3JlYXRlIGEgbmV3IG1vZHVsZSAoYW5kIHB1dCBpdCBpbnRvIHRoZSBjYWNoZSlcblx0dmFyIG1vZHVsZSA9IF9fd2VicGFja19tb2R1bGVfY2FjaGVfX1ttb2R1bGVJZF0gPSB7XG5cdFx0Ly8gbm8gbW9kdWxlLmlkIG5lZWRlZFxuXHRcdC8vIG5vIG1vZHVsZS5sb2FkZWQgbmVlZGVkXG5cdFx0ZXhwb3J0czoge31cblx0fTtcblxuXHQvLyBFeGVjdXRlIHRoZSBtb2R1bGUgZnVuY3Rpb25cblx0X193ZWJwYWNrX21vZHVsZXNfX1ttb2R1bGVJZF0obW9kdWxlLCBtb2R1bGUuZXhwb3J0cywgX193ZWJwYWNrX3JlcXVpcmVfXyk7XG5cblx0Ly8gUmV0dXJuIHRoZSBleHBvcnRzIG9mIHRoZSBtb2R1bGVcblx0cmV0dXJuIG1vZHVsZS5leHBvcnRzO1xufVxuXG4iLCIvLyBkZWZpbmUgZ2V0dGVyIGZ1bmN0aW9ucyBmb3IgaGFybW9ueSBleHBvcnRzXG5fX3dlYnBhY2tfcmVxdWlyZV9fLmQgPSAoZXhwb3J0cywgZGVmaW5pdGlvbikgPT4ge1xuXHRmb3IodmFyIGtleSBpbiBkZWZpbml0aW9uKSB7XG5cdFx0aWYoX193ZWJwYWNrX3JlcXVpcmVfXy5vKGRlZmluaXRpb24sIGtleSkgJiYgIV9fd2VicGFja19yZXF1aXJlX18ubyhleHBvcnRzLCBrZXkpKSB7XG5cdFx0XHRPYmplY3QuZGVmaW5lUHJvcGVydHkoZXhwb3J0cywga2V5LCB7IGVudW1lcmFibGU6IHRydWUsIGdldDogZGVmaW5pdGlvbltrZXldIH0pO1xuXHRcdH1cblx0fVxufTsiLCJfX3dlYnBhY2tfcmVxdWlyZV9fLm8gPSAob2JqLCBwcm9wKSA9PiAoT2JqZWN0LnByb3RvdHlwZS5oYXNPd25Qcm9wZXJ0eS5jYWxsKG9iaiwgcHJvcCkpIiwiLy8gZGVmaW5lIF9fZXNNb2R1bGUgb24gZXhwb3J0c1xuX193ZWJwYWNrX3JlcXVpcmVfXy5yID0gKGV4cG9ydHMpID0+IHtcblx0aWYodHlwZW9mIFN5bWJvbCAhPT0gJ3VuZGVmaW5lZCcgJiYgU3ltYm9sLnRvU3RyaW5nVGFnKSB7XG5cdFx0T2JqZWN0LmRlZmluZVByb3BlcnR5KGV4cG9ydHMsIFN5bWJvbC50b1N0cmluZ1RhZywgeyB2YWx1ZTogJ01vZHVsZScgfSk7XG5cdH1cblx0T2JqZWN0LmRlZmluZVByb3BlcnR5KGV4cG9ydHMsICdfX2VzTW9kdWxlJywgeyB2YWx1ZTogdHJ1ZSB9KTtcbn07IiwiaW1wb3J0IHsgaW5qZWN0Q3VzdG9tSnMsIHJlYWR5IH0gZnJvbSAnLi4vZnVuY3Rpb24vaW5qZWN0b3InO1xyXG5cclxuY29uc3QgaXNTdWRva3VQYWdlID0gL2hjYXB0Y2hhXFxTKlxcLmNvbSguKj8pZnJhbWU9Y2hhbGxlbmdlLy50ZXN0KGxvY2F0aW9uLmhyZWYpXHJcbmNvbnN0IGlzQ2hlY2tib3hQYWdlID0gL2hjYXB0Y2hhXFxTKlxcLmNvbSguKj8pZnJhbWU9Y2hlY2tib3gvLnRlc3QobG9jYXRpb24uaHJlZilcclxuaWYgKGlzU3Vkb2t1UGFnZSB8fCBpc0NoZWNrYm94UGFnZSkge1xyXG4gICAgY29uc29sZS5sb2coJ2Z1Y2swJylcclxuICAgIHJlYWR5KCgpID0+IGluamVjdEN1c3RvbUpzKFwiY29udGVudC9pbmplY3RlZC5qc1wiKSlcclxufVxyXG4iXSwibmFtZXMiOltdLCJzb3VyY2VSb290IjoiIn0=