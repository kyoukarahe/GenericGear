// Host ownership/lifetime, not a kinematic solver. Used by the actual page and tested without a DOM.
import {readDifferentialReplay,readDifferentialObservation} from "../../../packages/replay/dist/differential.js";
export class DifferentialSession {
  #epoch=0;#definition=null;#instance=null;#current=null;#disposed=false;
  constructor(digest){this.digest=digest;}
  get epoch(){return this.#epoch;}
  get definition(){return this.#definition;}
  get current(){return this.#current;}
  #clear(){this.#current=null;this.#definition?.dispose();this.#instance=null;this.#definition=null;}
  invalidate(){this.#epoch++;this.#clear();}
  async load(getBytes,observation=false){
    if(this.#disposed)throw Error("Session disposed.");const ticket=++this.#epoch;this.#clear();let candidate=null;
    try{
      const bytes=await getBytes();if(ticket!==this.#epoch||this.#disposed)return false;
      if(observation){const value=readDifferentialObservation(bytes);if(ticket!==this.#epoch)return false;this.#current={kind:"observation",value};return true;}
      candidate=readDifferentialReplay(bytes);await candidate.verifyIntegrity(this.digest);
      if(ticket!==this.#epoch||this.#disposed){candidate.dispose();return false;}
      this.#definition=candidate;this.#instance=candidate.createInstance("viewport");this.#current=null;return true;
    }catch(error){candidate?.dispose();if(ticket!==this.#epoch||this.#disposed)return false;this.#clear();throw error;}
  }
  apply(snapshot){if(this.#disposed)throw Error("Session disposed.");this.#current=null;if(!this.#instance)throw Error("Executable source required; stored analysis has no pose.");
    const value=this.#instance.evaluate(snapshot);this.#current={kind:"evaluation",value};return value;}
  clearEvaluation(){this.#current=null;}
  dispose(){this.#disposed=true;this.invalidate();}
}
