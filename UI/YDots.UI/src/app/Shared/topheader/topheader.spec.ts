import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TopheaderComponent } from './topheader';

describe('Topheader', () => {
  let component: TopheaderComponent;
  let fixture: ComponentFixture<TopheaderComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TopheaderComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(TopheaderComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
